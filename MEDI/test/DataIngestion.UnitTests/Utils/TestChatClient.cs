using Microsoft.Extensions.AI;

namespace CommunityToolkit.DataIngestion.UnitTests.Utils;

/// <summary>
/// A fake IChatClient for deterministic unit testing (no real LLM calls).
/// </summary>
public sealed class TestChatClient : IChatClient, IDisposable
{
    public Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>?
        GetResponseAsyncCallback { get; set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (GetResponseAsyncCallback is not null)
            return GetResponseAsyncCallback(messages, options, cancellationToken);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    /// <summary>Creates a TestChatClient that always returns the given JSON string.</summary>
    public static TestChatClient WithJsonResponse(string json)
        => new()
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)))
        };

    /// <summary>Creates a TestChatClient that always throws the given exception.</summary>
    public static TestChatClient WithException(Exception ex)
        => new()
        {
            GetResponseAsyncCallback = (_, _, _) => throw ex
        };
}
