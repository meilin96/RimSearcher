using System.Text.Json;
using System.Collections.Concurrent;
using RimSearcher.Server.Tools;

namespace RimSearcher.Server;

public sealed class RimSearcher
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IJsonRpcOutput _defaultOutput;

    private readonly SemaphoreSlim _concurrencyLimit = new(10, 10);

    public RimSearcher(TextWriter? protocolOut = null, bool emitLogNotifications = true)
    {
        _defaultOutput = new TextWriterJsonRpcOutput(protocolOut ?? Console.Out, _writeLock);

        if (emitLogNotifications)
        {
            ServerLogger.OnLogAsync = (msg, level) => LogAsync(msg, level);
        }
        else
        {
            ServerLogger.UseConsoleLogging();
        }
    }

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public async Task<IReadOnlyList<string>> HandleMessageAsync(string line, CancellationToken cancellationToken = default)
    {
        var output = new BufferedJsonRpcOutput();
        await ProcessMessageAsync(line, output, cancellationToken);
        return output.Messages;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            var line = await Console.In.ReadLineAsync();
            if (line == null) break;

            _ = Task.Run(() => ProcessMessageAsync(line, _defaultOutput, CancellationToken.None));
        }
    }

    private async Task ProcessMessageAsync(string line, IJsonRpcOutput output, CancellationToken cancellationToken)
    {
        await _concurrencyLimit.WaitAsync(cancellationToken);

        object? requestId = null;
        string? requestKey = null;
        CancellationTokenSource? cts = null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("method", out var methodProp))
            {
                if (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _))
                {
                    return;
                }

                if (root.TryGetProperty("id", out var errId))
                    await SendResponseAsync(output, errId, error: new { code = -32600, message = "Invalid Request" });
                return;
            }

            var method = methodProp.GetString();

            if (method == "$.cancelRequest")
            {
                if (root.TryGetProperty("params", out var p) && p.TryGetProperty("id", out var cancelId))
                {
                    var idToCancel = cancelId.ToString();
                    if (_activeRequests.TryRemove(idToCancel, out var targetCts))
                    {
                        targetCts.Cancel();
                    }
                }
                return;
            }

            var requestToken = cancellationToken;
            bool hasId = root.TryGetProperty("id", out var idProp);
            if (hasId)
            {
                requestId = idProp;
                requestKey = idProp.ToString();
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestToken = cts.Token;
                _activeRequests[requestKey] = cts;
            }

            await HandleRequestAsync(output, method, requestId, root, requestToken);
        }
        catch (JsonException)
        {
            await SendResponseAsync(output, null, error: new { code = -32700, message = "Parse error" });
        }
        catch (OperationCanceledException)
        {
            if (requestId != null)
                await SendResponseAsync(output, requestId, error: new { code = -32000, message = "Request cancelled" });
        }
        catch (Exception ex)
        {
            if (requestId != null)
                await SendResponseAsync(output, requestId, error: new { code = -32603, message = $"Internal error: {ex.Message}" });
        }
        finally
        {
            if (requestKey != null) _activeRequests.TryRemove(requestKey, out _);
            cts?.Dispose();
            _concurrencyLimit.Release();
        }
    }

    private async Task HandleRequestAsync(IJsonRpcOutput output, string? method, object? id, JsonElement root, CancellationToken ct)
    {
        try
        {
            if (method == "initialize")
            {
                await SendResponseAsync(output, id, new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new
                    {
                        tools = new { },
                        logging = new { },
                        progress = new { }
                    },
                    serverInfo = new
                    {
                        name = "RimSearcher-Server",
                        version = UpdateChecker.CurrentVersion,
                        description = "Specialized MCP server for deep RimWorld source code and XML Def analysis."
                    }
                });
            }
            else if (method == "notifications/initialized")
            {
                await SendLogNotificationAsync(output, "RimSearcher: Server initialized and ready to handle requests.", "info");
            }
            else if (method == "list_tools" || method == "tools/list")
            {
                if (id == null) return;
                await SendResponseAsync(output, id, new
                {
                    tools = _tools.Values.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        inputSchema = t.JsonSchema
                    })
                });
            }
            else if (method == "call_tool" || method == "tools/call")
            {
                if (id == null) return;
                var paramsElem = root.GetProperty("params");
                var toolName = paramsElem.GetProperty("name").GetString();

                if (toolName != null && _tools.TryGetValue(toolName, out var tool))
                {

                    var progressReporter = new Progress<double>(async p =>
                    {
                        await SendNotificationAsync(output, "notifications/progress", new
                        {
                            progress = p,
                            total = 1.0,
                            progressToken = id
                        });
                    });

                    var result = await tool.ExecuteAsync(paramsElem.GetProperty("arguments"), ct, progressReporter);
                    await SendResponseAsync(output, id, new
                    {
                        content = new[] { new { type = "text", text = result.Content } },
                        isError = result.IsError
                    });
                }
                else
                {
                    await SendResponseAsync(output, id, error: new { code = -32601, message = "Tool not found" });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (id != null)
                await SendResponseAsync(output, id, error: new { code = -32603, message = $"Internal error: {ex.Message}" });
        }
    }

    public async Task LogAsync(string message, string level = "info", string? logger = "RimSearcher")
    {
        await SendLogNotificationAsync(_defaultOutput, message, level, logger);
    }

    private async Task SendLogNotificationAsync(
        IJsonRpcOutput output,
        string message,
        string level = "info",
        string? logger = "RimSearcher")
    {
        if (string.Equals(logger, "RimSearcher", StringComparison.Ordinal) && TrySplitComponentMessage(message, out var component, out var normalizedMessage))
        {
            logger = component;
            message = normalizedMessage;
        }

        await SendNotificationAsync(output, "notifications/logging/message", new
        {
            level = level,
            logger = logger,
            data = message
        });
    }

    private static bool TrySplitComponentMessage(string message, out string component, out string normalizedMessage)
    {
        component = string.Empty;
        normalizedMessage = message;

        if (string.IsNullOrWhiteSpace(message)) return false;

        var separatorIndex = message.IndexOf(": ", StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex > 40) return false;

        var prefix = message[..separatorIndex];
        if (prefix.Any(ch => !char.IsLetterOrDigit(ch) && ch != '.' && ch != '_' && ch != '-'))
            return false;

        var suffix = message[(separatorIndex + 2)..].Trim();
        if (string.IsNullOrWhiteSpace(suffix)) return false;

        component = prefix;
        normalizedMessage = suffix;
        return true;
    }

    private static async Task SendNotificationAsync(IJsonRpcOutput output, string method, object? @params = null)
    {
        var notification = new { jsonrpc = "2.0", method = method, @params = @params };
        var json = JsonSerializer.Serialize(notification);
        await output.WriteLineAsync(json);
    }

    private static async Task SendResponseAsync(IJsonRpcOutput output, object? id, object? result = null, object? error = null)
    {
        if (id == null && error == null) return;

        object response = error != null
            ? new { jsonrpc = "2.0", id = id, error = error }
            : new { jsonrpc = "2.0", id = id, result = result };

        var json = JsonSerializer.Serialize(response);
        await output.WriteLineAsync(json);
    }
}
