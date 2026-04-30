# RimSearcher Streamable HTTP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an explicit `stdio` / `streamable-http` transport selector so one manually started local HTTP RimSearcher process can be shared by multiple URL-capable MCP clients while preserving the existing stdio default.

**Architecture:** Keep the existing index/bootstrap/tool behavior. Refactor `RimSearcher` just enough to process one JSON-RPC message into a buffered output, then add a minimal ASP.NET Core endpoint that maps `POST /mcp` to that runtime path. Do not add stdio proxying, auto-launch, config fingerprinting, or service discovery.

**Tech Stack:** .NET 10, C# 14, existing hand-written JSON-RPC runtime, ASP.NET Core minimal hosting, xUnit tests.

---

## File Structure

- Modify `Sources/RimSearcher.Server/RimSearcher.Server.csproj`: add ASP.NET Core framework reference and keep publish settings intact.
- Create `Sources/RimSearcher.Server/ServerCliOptions.cs`: parse `--transport`, `--host`, `--port`, and `--mount-path` without adding a command-line package.
- Create `Sources/RimSearcher.Server/JsonRpcOutput.cs`: abstract protocol output for stdout and buffered HTTP handling.
- Modify `Sources/RimSearcher.Server/RimSearcher.cs`: reuse current request routing for stdio and HTTP by adding a public single-message handling method.
- Modify `Sources/RimSearcher.Server/ServerLogger.cs`: expose console logging so HTTP mode can avoid emitting MCP logging notifications to a nonexistent stream.
- Create `Sources/RimSearcher.Server/McpHttpHost.cs`: host the Streamable HTTP request-response endpoint.
- Modify `Sources/RimSearcher.Server/Program.cs`: parse CLI options, build the existing index/runtime once, then choose stdio or HTTP transport.
- Create `Sources/RimSearcher.Server.Tests/RimSearcher.Server.Tests.csproj`: focused test project.
- Create `Sources/RimSearcher.Server.Tests/ServerCliOptionsTests.cs`: CLI parser coverage.
- Create `Sources/RimSearcher.Server.Tests/RimSearcherRuntimeTests.cs`: JSON-RPC runtime coverage without RimWorld source data.
- Create `Sources/RimSearcher.Server.Tests/McpHttpHostTests.cs`: HTTP endpoint behavior coverage.
- Modify `Sources/RimSearcher.slnx`: include the test project.
- Modify `README.md`: document shared local HTTP usage and URL client configuration.

---

### Task 1: Add Test Project And CLI Parser Tests

**Files:**
- Create: `Sources/RimSearcher.Server.Tests/RimSearcher.Server.Tests.csproj`
- Create: `Sources/RimSearcher.Server.Tests/ServerCliOptionsTests.cs`
- Modify: `Sources/RimSearcher.slnx`

- [ ] **Step 1: Create the test project file**

Add `Sources/RimSearcher.Server.Tests/RimSearcher.Server.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <LangVersion>14</LangVersion>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <FrameworkReference Include="Microsoft.AspNetCore.App" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
        <PackageReference Include="xunit" Version="2.9.3" />
        <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\RimSearcher.Server\RimSearcher.Server.csproj" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the test project to the solution file**

Change `Sources/RimSearcher.slnx` to:

```xml
<Solution>
  <Project Path="RimSearcher.Core/RimSearcher.Core.csproj" />
  <Project Path="RimSearcher.Server/RimSearcher.Server.csproj" />
  <Project Path="RimSearcher.Server.Tests/RimSearcher.Server.Tests.csproj" />
</Solution>
```

- [ ] **Step 3: Write failing CLI parser tests**

Add `Sources/RimSearcher.Server.Tests/ServerCliOptionsTests.cs`:

```csharp
using RimSearcher.Server;

namespace RimSearcher.Server.Tests;

public sealed class ServerCliOptionsTests
{
    [Fact]
    public void Parse_DefaultsToStdioAndLocalHttpOptions()
    {
        var options = ServerCliOptions.Parse([]);

        Assert.Equal(McpTransportKind.Stdio, options.Transport);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(51234, options.Port);
        Assert.Equal("/mcp", options.MountPath);
    }

    [Fact]
    public void Parse_ReadsExplicitStreamableHttpOptions()
    {
        var options = ServerCliOptions.Parse([
            "--transport", "streamable-http",
            "--host", "localhost",
            "--port", "3000",
            "--mount-path", "mcp"
        ]);

        Assert.Equal(McpTransportKind.StreamableHttp, options.Transport);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(3000, options.Port);
        Assert.Equal("/mcp", options.MountPath);
    }

    [Fact]
    public void Parse_AllowsEqualsSyntax()
    {
        var options = ServerCliOptions.Parse([
            "--transport=streamable-http",
            "--host=127.0.0.1",
            "--port=51235",
            "--mount-path=/custom-mcp"
        ]);

        Assert.Equal(McpTransportKind.StreamableHttp, options.Transport);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(51235, options.Port);
        Assert.Equal("/custom-mcp", options.MountPath);
    }

    [Fact]
    public void Parse_RejectsUnsupportedTransport()
    {
        var ex = Assert.Throws<ArgumentException>(() => ServerCliOptions.Parse(["--transport", "sse"]));

        Assert.Contains("Unsupported transport", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsInvalidPort()
    {
        var ex = Assert.Throws<ArgumentException>(() => ServerCliOptions.Parse(["--port", "abc"]));

        Assert.Contains("Invalid port", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj
```

Expected: build fails because `ServerCliOptions` and `McpTransportKind` do not exist.

- [ ] **Step 5: Commit**

```powershell
git add Sources\RimSearcher.slnx Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj Sources\RimSearcher.Server.Tests\ServerCliOptionsTests.cs
git commit -m "test: add server cli option coverage"
```

---

### Task 2: Implement CLI Option Parsing

**Files:**
- Create: `Sources/RimSearcher.Server/ServerCliOptions.cs`

- [ ] **Step 1: Add the parser implementation**

Create `Sources/RimSearcher.Server/ServerCliOptions.cs`:

```csharp
namespace RimSearcher.Server;

public enum McpTransportKind
{
    Stdio,
    StreamableHttp
}

public sealed record ServerCliOptions(
    McpTransportKind Transport,
    string Host,
    int Port,
    string MountPath)
{
    public static ServerCliOptions Parse(string[] args)
    {
        var transport = McpTransportKind.Stdio;
        var host = "127.0.0.1";
        var port = 51234;
        var mountPath = "/mcp";

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = SplitOption(args[i]);
            var value = inlineValue;

            switch (name)
            {
                case "--transport":
                    value ??= ReadValue(args, ref i, name);
                    transport = ParseTransport(value);
                    break;
                case "--host":
                    value ??= ReadValue(args, ref i, name);
                    host = value;
                    break;
                case "--port":
                    value ??= ReadValue(args, ref i, name);
                    if (!int.TryParse(value, out port) || port < 1 || port > 65535)
                    {
                        throw new ArgumentException($"Invalid port '{value}'. Port must be between 1 and 65535.");
                    }
                    break;
                case "--mount-path":
                    value ??= ReadValue(args, ref i, name);
                    mountPath = NormalizeMountPath(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{name}'.");
            }
        }

        return new ServerCliOptions(transport, host, port, mountPath);
    }

    private static (string Name, string? Value) SplitOption(string arg)
    {
        var equalsIndex = arg.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            return (arg, null);
        }

        return (arg[..equalsIndex], arg[(equalsIndex + 1)..]);
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }

    private static McpTransportKind ParseTransport(string value)
    {
        return value switch
        {
            "stdio" => McpTransportKind.Stdio,
            "streamable-http" => McpTransportKind.StreamableHttp,
            _ => throw new ArgumentException($"Unsupported transport '{value}'. Use 'stdio' or 'streamable-http'.")
        };
    }

    private static string NormalizeMountPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Mount path cannot be empty.");
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith('/', StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }
}
```

- [ ] **Step 2: Run CLI parser tests**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj --filter ServerCliOptionsTests
```

Expected: all `ServerCliOptionsTests` pass.

- [ ] **Step 3: Commit**

```powershell
git add Sources\RimSearcher.Server\ServerCliOptions.cs
git commit -m "feat: parse server transport options"
```

---

### Task 3: Add Runtime Message Handling Tests

**Files:**
- Create: `Sources/RimSearcher.Server.Tests/RimSearcherRuntimeTests.cs`

- [ ] **Step 1: Write failing runtime tests**

Add `Sources/RimSearcher.Server.Tests/RimSearcherRuntimeTests.cs`:

```csharp
using System.Text.Json;
using RimSearcher.Server;
using RimSearcher.Server.Tools;

namespace RimSearcher.Server.Tests;

public sealed class RimSearcherRuntimeTests
{
    [Fact]
    public async Task HandleMessageAsync_ReturnsInitializeResponse()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);

        var messages = await server.HandleMessageAsync("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
            """);

        var response = Assert.Single(messages);
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("2025-11-25", root.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal("RimSearcher-Server", root.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandleMessageAsync_ReturnsRegisteredTools()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);
        server.RegisterTool(new FakeTool());

        var messages = await server.HandleMessageAsync("""
            {"jsonrpc":"2.0","id":"tools","method":"tools/list","params":{}}
            """);

        var response = Assert.Single(messages);
        using var document = JsonDocument.Parse(response);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools");

        Assert.Single(tools.EnumerateArray());
        Assert.Equal("fake_tool", tools[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandleMessageAsync_ReturnsParseErrorForInvalidJson()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);

        var messages = await server.HandleMessageAsync("{");

        var response = Assert.Single(messages);
        using var document = JsonDocument.Parse(response);

        Assert.True(document.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(-32700, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task HandleMessageAsync_IgnoresJsonRpcResponseBodies()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);

        var messages = await server.HandleMessageAsync("""
            {"jsonrpc":"2.0","id":1,"result":{}}
            """);

        Assert.Empty(messages);
    }

    private sealed class FakeTool : ITool
    {
        public string Name => "fake_tool";
        public string Description => "Fake tool for runtime tests.";
        public object JsonSchema => new { type = "object", additionalProperties = false };

        public Task<ToolResult> ExecuteAsync(
            JsonElement arguments,
            CancellationToken cancellationToken,
            IProgress<double>? progress = null)
        {
            return Task.FromResult(new ToolResult("fake result"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj --filter RimSearcherRuntimeTests
```

Expected: build fails because `RimSearcher` does not have the `emitLogNotifications` constructor parameter or `HandleMessageAsync`.

- [ ] **Step 3: Commit**

```powershell
git add Sources\RimSearcher.Server.Tests\RimSearcherRuntimeTests.cs
git commit -m "test: cover json rpc runtime handling"
```

---

### Task 4: Refactor Runtime Output For Stdio And HTTP

**Files:**
- Create: `Sources/RimSearcher.Server/JsonRpcOutput.cs`
- Modify: `Sources/RimSearcher.Server/ServerLogger.cs`
- Modify: `Sources/RimSearcher.Server/RimSearcher.cs`

- [ ] **Step 1: Add protocol output abstractions**

Create `Sources/RimSearcher.Server/JsonRpcOutput.cs`:

```csharp
namespace RimSearcher.Server;

public interface IJsonRpcOutput
{
    Task WriteLineAsync(string json);
}

public sealed class TextWriterJsonRpcOutput : IJsonRpcOutput
{
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _writeLock;

    public TextWriterJsonRpcOutput(TextWriter writer, SemaphoreSlim writeLock)
    {
        _writer = writer;
        _writeLock = writeLock;
    }

    public async Task WriteLineAsync(string json)
    {
        await _writeLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

public sealed class BufferedJsonRpcOutput : IJsonRpcOutput
{
    private readonly List<string> _messages = new();

    public IReadOnlyList<string> Messages => _messages;

    public Task WriteLineAsync(string json)
    {
        _messages.Add(json);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Expose console logging in `ServerLogger`**

Modify `Sources/RimSearcher.Server/ServerLogger.cs` so the class becomes:

```csharp
namespace RimSearcher.Server;

public static class ServerLogger
{
    public static Func<string, string, Task>? OnLogAsync;

    public static void UseConsoleLogging()
    {
        OnLogAsync = WriteConsoleAsync;
    }

    public static async Task LogAsync(string message, string level = "info")
    {
        if (OnLogAsync != null)
        {
            await OnLogAsync(message, level);
        }
        else
        {
            await WriteConsoleAsync(message, level);
        }
    }

    public static async Task Info(string component, string message, params (string Key, object? Value)[] fields)
        => await LogAsync(Format(component, message, fields), "info");

    public static async Task Warning(string component, string message, params (string Key, object? Value)[] fields)
        => await LogAsync(Format(component, message, fields), "warning");

    public static async Task Error(string component, string message, params (string Key, object? Value)[] fields)
        => await LogAsync(Format(component, message, fields), "error");

    private static Task WriteConsoleAsync(string message, string level)
    {
        return Console.Error.WriteLineAsync($"[{level.ToUpperInvariant()}] {message}");
    }

    private static string Format(string component, string message, (string Key, object? Value)[] fields)
    {
        var normalizedMessage = Sanitize(message);
        if (fields == null || fields.Length == 0)
        {
            return $"{component}: {normalizedMessage}";
        }

        var parts = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .Select(field => $"{field.Key}={Sanitize(field.Value?.ToString() ?? "null")}")
            .ToArray();

        if (parts.Length == 0)
        {
            return $"{component}: {normalizedMessage}";
        }

        return $"{component}: {normalizedMessage} | {string.Join(", ", parts)}";
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
```

- [ ] **Step 3: Refactor `RimSearcher` around `IJsonRpcOutput`**

In `Sources/RimSearcher.Server/RimSearcher.cs`, keep the class name and tool behavior, and make these concrete changes:

Replace the `_protocolOut` field with:

```csharp
private readonly IJsonRpcOutput _defaultOutput;
```

Replace the constructor with:

```csharp
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
```

Add the public HTTP/test entry point:

```csharp
public async Task<IReadOnlyList<string>> HandleMessageAsync(string line, CancellationToken cancellationToken = default)
{
    var output = new BufferedJsonRpcOutput();
    await ProcessMessageAsync(line, output, cancellationToken);
    return output.Messages;
}
```

Replace `RunAsync` with:

```csharp
public async Task RunAsync()
{
    while (true)
    {
        var line = await Console.In.ReadLineAsync();
        if (line == null) break;

        _ = Task.Run(() => ProcessMessageAsync(line, _defaultOutput, CancellationToken.None));
    }
}
```

Add this private method by moving the existing per-line logic out of `RunAsync`:

```csharp
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
```

Change `HandleRequestAsync` signature to:

```csharp
private async Task HandleRequestAsync(IJsonRpcOutput output, string? method, object? id, JsonElement root, CancellationToken ct)
```

Inside `HandleRequestAsync`, replace every `SendResponseAsync(...)` call with `SendResponseAsync(output, ...)`, every `SendNotificationAsync(...)` call with `SendNotificationAsync(output, ...)`, and replace:

```csharp
await LogAsync("RimSearcher: Server initialized and ready to handle requests.", "info");
```

with:

```csharp
await SendLogNotificationAsync(output, "RimSearcher: Server initialized and ready to handle requests.", "info");
```

Replace `LogAsync`, `SendNotificationAsync`, and `SendResponseAsync` with:

```csharp
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
```

- [ ] **Step 4: Run runtime tests**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj --filter RimSearcherRuntimeTests
```

Expected: all `RimSearcherRuntimeTests` pass.

- [ ] **Step 5: Run all tests**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Sources\RimSearcher.Server\JsonRpcOutput.cs Sources\RimSearcher.Server\ServerLogger.cs Sources\RimSearcher.Server\RimSearcher.cs
git commit -m "feat: buffer json rpc runtime output"
```

---

### Task 5: Add HTTP Endpoint Tests

**Files:**
- Create: `Sources/RimSearcher.Server.Tests/McpHttpHostTests.cs`

- [ ] **Step 1: Write failing HTTP endpoint tests**

Add `Sources/RimSearcher.Server.Tests/McpHttpHostTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RimSearcher.Server;

namespace RimSearcher.Server.Tests;

public sealed class McpHttpHostTests
{
    [Fact]
    public async Task HandlePostAsync_ReturnsJsonRpcResponse()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);
        var context = CreateContext("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
            """);

        await McpHttpHost.HandlePostAsync(context, server);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);

        var body = await ReadResponseBodyAsync(context);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("RimSearcher-Server", document.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandlePostAsync_ReturnsAcceptedForNotification()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);
        var context = CreateContext("""
            {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
            """);

        await McpHttpHost.HandlePostAsync(context, server);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Equal(string.Empty, await ReadResponseBodyAsync(context));
    }

    [Fact]
    public async Task HandlePostAsync_ReturnsAcceptedForJsonRpcResponseBody()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);
        var context = CreateContext("""
            {"jsonrpc":"2.0","id":1,"result":{}}
            """);

        await McpHttpHost.HandlePostAsync(context, server);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Equal(string.Empty, await ReadResponseBodyAsync(context));
    }

    [Fact]
    public async Task HandleGetAsync_ReturnsMethodNotAllowed()
    {
        var context = CreateContext(string.Empty);

        await McpHttpHost.HandleGetAsync(context);

        Assert.Equal(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandlePostAsync_RejectsNonLocalhostOrigin()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);
        var context = CreateContext("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
            """, "https://example.com");

        await McpHttpHost.HandlePostAsync(context, server);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(string.Empty, await ReadResponseBodyAsync(context));
    }

    [Fact]
    public async Task HandlePostAsync_AllowsLoopbackOrigin()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);
        var context = CreateContext("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
            """, "http://127.0.0.1:51234");

        await McpHttpHost.HandlePostAsync(context, server);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(string body, string? origin = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();

        if (!string.IsNullOrWhiteSpace(origin))
        {
            context.Request.Headers.Origin = origin;
        }

        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj --filter McpHttpHostTests
```

Expected: build fails because `McpHttpHost` does not exist.

- [ ] **Step 3: Commit**

```powershell
git add Sources\RimSearcher.Server.Tests\McpHttpHostTests.cs
git commit -m "test: cover streamable http endpoint"
```

---

### Task 6: Implement Streamable HTTP Host

**Files:**
- Modify: `Sources/RimSearcher.Server/RimSearcher.Server.csproj`
- Create: `Sources/RimSearcher.Server/McpHttpHost.cs`

- [ ] **Step 1: Add ASP.NET Core framework reference**

Modify `Sources/RimSearcher.Server/RimSearcher.Server.csproj` by adding this item group after the existing `ProjectReference` item group:

```xml
    <ItemGroup>
      <FrameworkReference Include="Microsoft.AspNetCore.App" />
    </ItemGroup>
```

- [ ] **Step 2: Add HTTP host implementation**

Create `Sources/RimSearcher.Server/McpHttpHost.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace RimSearcher.Server;

public static class McpHttpHost
{
    public static async Task RunAsync(RimSearcher server, ServerCliOptions options, CancellationToken cancellationToken = default)
    {
        var app = Build(server, options);
        await app.RunAsync(cancellationToken);
    }

    public static WebApplication Build(RimSearcher server, ServerCliOptions options)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");

        var app = builder.Build();
        var mountPath = NormalizeMountPath(options.MountPath);

        app.MapGet(mountPath, HandleGetAsync);
        app.MapPost(mountPath, context => HandlePostAsync(context, server));

        return app;
    }

    public static Task HandleGetAsync(HttpContext context)
    {
        if (!IsAllowedOrigin(context.Request.Headers.Origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return Task.CompletedTask;
    }

    public static async Task HandlePostAsync(HttpContext context, RimSearcher server)
    {
        if (!IsAllowedOrigin(context.Request.Headers.Origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        var messages = await server.HandleMessageAsync(body, context.RequestAborted);
        var response = FindJsonRpcResponse(messages);

        if (response == null)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(response, Encoding.UTF8, context.RequestAborted);
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static string? FindJsonRpcResponse(IReadOnlyList<string> messages)
    {
        foreach (var message in messages)
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var hasId = root.TryGetProperty("id", out _);
            var hasResult = root.TryGetProperty("result", out _);
            var hasError = root.TryGetProperty("error", out _);

            if (hasId && (hasResult || hasError))
            {
                return message;
            }
        }

        return null;
    }

    private static string NormalizeMountPath(string mountPath)
    {
        return mountPath.StartsWith('/', StringComparison.Ordinal) ? mountPath : "/" + mountPath;
    }
}
```

- [ ] **Step 3: Run HTTP endpoint tests**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj --filter McpHttpHostTests
```

Expected: all `McpHttpHostTests` pass.

- [ ] **Step 4: Run all tests**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Sources\RimSearcher.Server\RimSearcher.Server.csproj Sources\RimSearcher.Server\McpHttpHost.cs
git commit -m "feat: add streamable http endpoint"
```

---

### Task 7: Wire Transport Selection In Program

**Files:**
- Modify: `Sources/RimSearcher.Server/Program.cs`

- [ ] **Step 1: Parse CLI options at startup**

In `Sources/RimSearcher.Server/Program.cs`, after the encoding lines:

```csharp
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
```

add:

```csharp
var cliOptions = ServerCliOptions.Parse(args);
```

- [ ] **Step 2: Select protocol output based on transport**

Replace:

```csharp
var protocolOut = Console.Out;
Console.SetOut(Console.Error);
```

with:

```csharp
var protocolOut = cliOptions.Transport == McpTransportKind.Stdio
    ? Console.Out
    : TextWriter.Null;
Console.SetOut(Console.Error);
```

- [ ] **Step 3: Pass logging mode into the runtime**

Replace:

```csharp
var server = new RimSearcher.Server.RimSearcher(protocolOut);
```

with:

```csharp
var server = new RimSearcher.Server.RimSearcher(
    protocolOut,
    emitLogNotifications: cliOptions.Transport == McpTransportKind.Stdio);
```

- [ ] **Step 4: Log selected HTTP endpoint before startup**

After the existing `RimSearcher MCP server started` log block:

```csharp
if (isLoaded && hasPaths)
{
    await ServerLogger.Info("Program", "RimSearcher MCP server started");
}
```

add:

```csharp
if (cliOptions.Transport == McpTransportKind.StreamableHttp)
{
    await ServerLogger.Info(
        "Program",
        "Streamable HTTP endpoint configured",
        ("url", $"http://{cliOptions.Host}:{cliOptions.Port}{cliOptions.MountPath}"));
}
```

- [ ] **Step 5: Start the selected transport**

Replace the final line:

```csharp
await server.RunAsync();
```

with:

```csharp
if (cliOptions.Transport == McpTransportKind.StreamableHttp)
{
    await McpHttpHost.RunAsync(server, cliOptions);
}
else
{
    await server.RunAsync();
}
```

- [ ] **Step 6: Run all tests**

Run:

```powershell
dotnet test Sources\RimSearcher.Server.Tests\RimSearcher.Server.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Build the solution**

Run:

```powershell
dotnet build Sources\RimSearcher.slnx
```

Expected: build succeeds with 0 errors.

- [ ] **Step 8: Commit**

```powershell
git add Sources\RimSearcher.Server\Program.cs
git commit -m "feat: select mcp transport at startup"
```

---

### Task 8: Update README For Shared HTTP Usage

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the architecture diagram text**

In `README.md`, under `RimSearcher Architecture (Narrow)`, replace:

```text
MCP Client 
  |
  | JSON-RPC (MCP)
  v
RimSearcher.cs (runtime)
```

with:

```text
MCP Client
  |
  | JSON-RPC over stdio or Streamable HTTP
  v
RimSearcher.cs (runtime)
```

- [ ] **Step 2: Update the startup flow**

In the `启动流程` list, replace item 7:

```markdown
7. 注册工具并启动 MCP 服务
```

with:

```markdown
7. 注册工具并按 `--transport` 选择启动 stdio 或 Streamable HTTP 服务
```

- [ ] **Step 3: Add shared HTTP instructions after stdio client examples**

After the OpenCode example block, add:

````markdown
#### 共享本地 HTTP 服务（支持 URL 的客户端）

默认 stdio 模式仍然兼容所有现有配置，但每个客户端会启动一个独立进程。若客户端支持 URL 形式的 MCP server，可以手动启动一次共享 HTTP 服务：

```powershell
$env:RIMSEARCHER_CONFIG="D:/your/custom/path/config.json"
D:/Tools/RimSearcher/RimSearcher.Server.exe --transport streamable-http --host 127.0.0.1 --port 51234 --mount-path /mcp
```

然后把支持 URL 的客户端指向：

```text
http://127.0.0.1:51234/mcp
```

HTTP 模式默认只绑定 `127.0.0.1`，推荐仅用于本机共享。若手动改成 `0.0.0.0`，需要自行承担局域网暴露风险；本项目当前不提供远程认证或授权机制。
````

- [ ] **Step 4: Update local verification text**

In the `本地验证` section, after the existing stdio validation bullets, add:

````markdown
HTTP 模式验证时，先启动：

```powershell
RimSearcher.Server.exe --transport streamable-http --host 127.0.0.1 --port 51234 --mount-path /mcp
```

再用支持 URL 的 MCP 客户端连接 `http://127.0.0.1:51234/mcp`，执行一次 `tools/list` 或 `locate` 即可确认共享服务可用。
````

- [ ] **Step 5: Run Markdown and build checks**

Run:

```powershell
dotnet build Sources\RimSearcher.slnx
```

Expected: build succeeds with 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add README.md
git commit -m "docs: document shared streamable http mode"
```

---

### Task 9: Manual Smoke Verification

**Files:**
- No source file changes.

- [ ] **Step 1: Build release executable**

Run:

```powershell
dotnet publish Sources\RimSearcher.Server\RimSearcher.Server.csproj -c Release
```

Expected: publish succeeds and writes the executable under `Sources\Publish\`.

- [ ] **Step 2: Verify stdio initialize still works**

Run:

```powershell
$request = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'
$request | Sources\Publish\RimSearcher.Server.exe
```

Expected: stdout contains one JSON-RPC response with `serverInfo.name` equal to `RimSearcher-Server`. Stderr may contain startup logs.

- [ ] **Step 3: Start HTTP server manually**

Run in one PowerShell window:

```powershell
Sources\Publish\RimSearcher.Server.exe --transport streamable-http --host 127.0.0.1 --port 51234 --mount-path /mcp
```

Expected: process remains running and stderr includes the configured endpoint URL.

- [ ] **Step 4: POST initialize to HTTP endpoint**

Run in a second PowerShell window:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://127.0.0.1:51234/mcp `
  -ContentType 'application/json' `
  -Body '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'
```

Expected: response includes `protocolVersion` equal to `2025-11-25` and `serverInfo.name` equal to `RimSearcher-Server`.

- [ ] **Step 5: Verify notification returns 202**

Run:

```powershell
Invoke-WebRequest `
  -Method Post `
  -Uri http://127.0.0.1:51234/mcp `
  -ContentType 'application/json' `
  -Body '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}'
```

Expected: HTTP status code is `202`.

- [ ] **Step 6: Verify GET returns 405**

Run:

```powershell
Invoke-WebRequest -Method Get -Uri http://127.0.0.1:51234/mcp -SkipHttpErrorCheck
```

Expected: HTTP status code is `405`.

- [ ] **Step 7: Commit verification notes only if a tracked doc changed**

If no files changed during smoke verification, do not create a commit. If README was adjusted based on the smoke output, commit with:

```powershell
git add README.md
git commit -m "docs: refine streamable http verification"
```
