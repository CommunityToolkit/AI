using Microsoft.Extensions.DataRetrieval;
using CommunityToolkit.DataRetrieval.UnitTests.Utils;

namespace CommunityToolkit.DataRetrieval.UnitTests.Retrieval;

public class HydeQueryTransformerTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient", () => new HydeQueryTransformer(null!));
    }

    [Fact]
    public async Task ProcessAsync_ReplacesVariantsWithHypothetical()
    {
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        "Dependency injection in .NET uses the IServiceCollection to register services.")))
        };
        var transformer = new HydeQueryTransformer(client);
        var query = new RetrievalQuery("How does DI work in .NET?");

        var output = await transformer.ProcessAsync(query);

        Assert.Single(output.Variants);
        Assert.Contains("Dependency injection", output.Variants[0]);
        Assert.Equal("How does DI work in .NET?", output.Text); // Original preserved
    }

    [Fact]
    public async Task ProcessAsync_StoresHydeMetadata()
    {
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        "A hypothetical answer about the topic.")))
        };
        var transformer = new HydeQueryTransformer(client);
        var query = new RetrievalQuery("some question");

        var output = await transformer.ProcessAsync(query);

        Assert.True(output.Metadata.ContainsKey("hyde_hypothetical"));
        Assert.Equal("A hypothetical answer about the topic.", output.Metadata["hyde_hypothetical"]);
    }

    [Fact]
    public async Task ProcessAsync_EmptyResponse_ReturnsOriginal()
    {
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant, "   ")))
        };
        var transformer = new HydeQueryTransformer(client);
        var query = new RetrievalQuery("my question");

        var output = await transformer.ProcessAsync(query);

        Assert.Single(output.Variants);
        Assert.Equal("my question", output.Variants[0]); // Unchanged
    }

    [Fact]
    public async Task ProcessAsync_LlmThrows_ReturnsOriginal()
    {
        using var client = TestChatClient.WithException(new InvalidOperationException("timeout"));
        var transformer = new HydeQueryTransformer(client);
        var query = new RetrievalQuery("my question");

        var output = await transformer.ProcessAsync(query);

        Assert.Single(output.Variants);
        Assert.Equal("my question", output.Variants[0]);
        Assert.False(output.Metadata.ContainsKey("hyde_hypothetical"));
    }
}
