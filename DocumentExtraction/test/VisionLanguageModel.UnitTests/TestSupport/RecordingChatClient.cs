using Microsoft.Extensions.AI;

namespace CommunityToolkit.DocumentExtraction.VisionLanguageModel.UnitTests;

internal sealed class RecordingChatClient(params object[] results) : IChatClient
{
    private readonly Queue<object> _results = new(results);

    public List<(ChatMessage[] Messages, ChatOptions? Options, CancellationToken Token)> Requests { get; } = [];
    public List<(Type Type, object? Key)> ServiceRequests { get; } = [];
    public Action<CancellationToken>? OnRequest { get; init; }
    public int DisposeCalls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add((messages.ToArray(), options, cancellationToken));
        OnRequest?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        object result = _results.Dequeue();
        return result is Exception error
            ? Task.FromException<ChatResponse>(error)
            : Task.FromResult((ChatResponse)result);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Vision must use the deterministic non-streaming seam.");

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ServiceRequests.Add((serviceType, serviceKey));
        return serviceType == typeof(IChatClient) && serviceKey is null ? this : null;
    }

    public void Dispose() => DisposeCalls++;

    public static ChatResponse Response(string text, object? raw = null, string? model = "actual-vision-model")
        => new(new ChatMessage(ChatRole.Assistant, text)) { RawRepresentation = raw, ModelId = model };
}

internal sealed class BorrowedInput(byte[] bytes, CancellationTokenSource? cancelDuringCopy = null)
    : MemoryStream(bytes, writable: false)
{
    public int DisposeCalls { get; private set; }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        cancelDuringCopy?.Cancel();
        return base.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        DisposeCalls++;
        base.Dispose(disposing);
    }

    public void AssertStillReadable(byte firstByte)
    {
        Assert.Equal(0, DisposeCalls);
        Position = 0;
        Assert.Equal(firstByte, ReadByte());
    }
}
