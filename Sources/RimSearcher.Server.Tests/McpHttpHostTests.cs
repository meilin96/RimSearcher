using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RimSearcher.Server;
using Xunit;

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
