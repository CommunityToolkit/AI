using Microsoft.Extensions.AI;

namespace CommunityToolkit.DataIngestion.VisionLMOcr.UnitTests.Utils;

public sealed class TestChatClient : IChatClient, IDisposable
{
    private readonly string responseText;

    public TestChatClient(string responseText) => this.responseText = responseText;

    public List<ChatMessage> LastMessages { get; private set; } = [];

    public object? UnknownService { get; set; }

    public Type? LastServiceType { get; private set; }

    public object? LastServiceKey { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastMessages = messages.ToList();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        LastServiceType = serviceType;
        LastServiceKey = serviceKey;
        return UnknownService;
    }

    public void Dispose()
    {
    }
}
