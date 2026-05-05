using Microsoft.Extensions.DataRetrieval;
using CommunityToolkit.DataRetrieval.UnitTests.Utils;

namespace CommunityToolkit.DataRetrieval.UnitTests.Retrieval;

public class LlmRerankerTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient", () => new LlmReranker(null!));
    }

    [Fact]
    public async Task ProcessAsync_TwoOrFewerChunks_ReturnsUnchanged()
    {
        using var client = TestChatClient.WithJsonResponse("""{"rankedIndices": [2, 1]}""");
        var reranker = new LlmReranker(client);
        var (results, query) = CreateResults("chunk1", "chunk2");

        var output = await reranker.ProcessAsync(results, query);

        Assert.Equal(2, output.Chunks.Count);
        Assert.Equal("chunk1", output.Chunks[0].Content);
    }

    [Fact]
    public async Task ProcessAsync_ReranksBasedOnLlmResponse()
    {
        using var client = TestChatClient.WithJsonResponse("""{"rankedIndices": [3, 1, 2]}""");
        var reranker = new LlmReranker(client);
        var (results, query) = CreateResults("A", "B", "C");

        var output = await reranker.ProcessAsync(results, query);

        Assert.Equal("C", output.Chunks[0].Content);
        Assert.Equal("A", output.Chunks[1].Content);
        Assert.Equal("B", output.Chunks[2].Content);
    }

    [Fact]
    public async Task ProcessAsync_FiltersOutOfRangeIndices()
    {
        using var client = TestChatClient.WithJsonResponse("""{"rankedIndices": [99, 1, -1, 2]}""");
        var reranker = new LlmReranker(client);
        var (results, query) = CreateResults("A", "B", "C");

        var output = await reranker.ProcessAsync(results, query);

        Assert.Equal(2, output.Chunks.Count);
        Assert.Equal("A", output.Chunks[0].Content);
        Assert.Equal("B", output.Chunks[1].Content);
    }

    [Fact]
    public async Task ProcessAsync_DeduplicatesIndices()
    {
        using var client = TestChatClient.WithJsonResponse("""{"rankedIndices": [2, 2, 2, 1]}""");
        var reranker = new LlmReranker(client);
        var (results, query) = CreateResults("A", "B", "C");

        var output = await reranker.ProcessAsync(results, query);

        Assert.Equal(2, output.Chunks.Count);
        Assert.Equal("B", output.Chunks[0].Content);
        Assert.Equal("A", output.Chunks[1].Content);
    }

    [Fact]
    public async Task ProcessAsync_FallsBackToTopCandidatesOnInvalidJson()
    {
        using var client = TestChatClient.WithJsonResponse("not valid json");
        var reranker = new LlmReranker(client);
        var (results, query) = CreateResults("A", "B", "C");

        var output = await reranker.ProcessAsync(results, query);

        // Fallback: top MaxResults candidates returned as-is
        Assert.True(output.Chunks.Count > 0);
        Assert.Equal("A", output.Chunks[0].Content);
    }

    [Fact]
    public async Task ProcessAsync_FallsBackToTopCandidatesOnException()
    {
        using var client = TestChatClient.WithException(new InvalidOperationException("LLM down"));
        var reranker = new LlmReranker(client);
        var (results, query) = CreateResults("A", "B", "C");

        var output = await reranker.ProcessAsync(results, query);

        Assert.True(output.Chunks.Count > 0);
        Assert.Equal("A", output.Chunks[0].Content);
    }

    [Fact]
    public async Task ProcessAsync_SetsRerankedMetadata()
    {
        using var client = TestChatClient.WithJsonResponse("""{"rankedIndices": [1, 2, 3]}""");
        var reranker = new LlmReranker(client);
        var (results, query) = CreateResults("A", "B", "C");

        var output = await reranker.ProcessAsync(results, query);

        Assert.True((bool)output.Metadata["reranked"]!);
        Assert.Equal(3, (int)output.Metadata["reranked_count"]!);
    }

    private static (RetrievalResults results, RetrievalQuery query) CreateResults(params string[] contents)
    {
        var query = new RetrievalQuery("test query");
        var results = new RetrievalResults
        {
            Chunks = contents.Select((c, i) => new RetrievalChunk(c, 1.0 - i * 0.1)).ToList()
        };
        return (results, query);
    }
}
