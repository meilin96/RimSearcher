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
