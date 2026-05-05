using Microsoft.Extensions.DataRetrieval;
using CommunityToolkit.DataRetrieval.UnitTests.Utils;

namespace CommunityToolkit.DataRetrieval.UnitTests.Retrieval;

public class CragValidatorTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient", () => new CragValidator(null!));
    }

    [Fact]
    public async Task ProcessAsync_EmptyChunks_ReturnsIncorrectPath()
    {
        using var client = TestChatClient.WithJsonResponse("""{"score": 5}""");
        var validator = new CragValidator(client);
        var query = new RetrievalQuery("test");
        var results = new RetrievalResults { Chunks = [] };

        var output = await validator.ProcessAsync(results, query);

        Assert.Equal(0, output.Metadata["crag_score"]);
        Assert.Equal("Incorrect", output.Metadata["crag_path"]);
        Assert.True((bool)output.Metadata["low_confidence"]!);
    }

    [Fact]
    public async Task ProcessAsync_HighScore_ReturnsCorrectPath()
    {
        using var client = TestChatClient.WithJsonResponse("""{"score": 5, "reasoning": "great match"}""");
        var validator = new CragValidator(client);
        var (results, query) = CreateResultsWithChunks(3);

        var output = await validator.ProcessAsync(results, query);

        Assert.Equal(5, output.Metadata["crag_score"]);
        Assert.Equal("Correct", output.Metadata["crag_path"]);
        Assert.False(output.Metadata.ContainsKey("low_confidence"));
    }

    [Fact]
    public async Task ProcessAsync_MidScore_ReturnsAmbiguousPath()
    {
        using var client = TestChatClient.WithJsonResponse("""{"score": 3, "reasoning": "partial match"}""");
        var validator = new CragValidator(client);
        var (results, query) = CreateResultsWithChunks(3);

        var output = await validator.ProcessAsync(results, query);

        Assert.Equal(3, output.Metadata["crag_score"]);
        Assert.Equal("Ambiguous", output.Metadata["crag_path"]);
        Assert.True((bool)output.Metadata["needs_followup"]!);
    }

    [Fact]
    public async Task ProcessAsync_LowScore_ReturnsIncorrectAndClearsChunks()
    {
        using var client = TestChatClient.WithJsonResponse("""{"score": 1, "reasoning": "off topic"}""");
        var validator = new CragValidator(client);
        var (results, query) = CreateResultsWithChunks(3);

        var output = await validator.ProcessAsync(results, query);

        Assert.Equal(1, output.Metadata["crag_score"]);
        Assert.Equal("Incorrect", output.Metadata["crag_path"]);
        Assert.True((bool)output.Metadata["low_confidence"]!);
        Assert.Empty(output.Chunks);
    }

    [Fact]
    public async Task ProcessAsync_ScoreOutOfRange_ClampedToDefault()
    {
        using var client = TestChatClient.WithJsonResponse("""{"score": 99}""");
        var validator = new CragValidator(client);
        var (results, query) = CreateResultsWithChunks(3);

        var output = await validator.ProcessAsync(results, query);

        // Score 99 is not in [1,5] → defaults to 3 (Ambiguous)
        Assert.Equal(3, output.Metadata["crag_score"]);
        Assert.Equal("Ambiguous", output.Metadata["crag_path"]);
    }

    [Fact]
    public async Task ProcessAsync_LlmThrows_DefaultsToAmbiguous()
    {
        using var client = TestChatClient.WithException(new InvalidOperationException("fail"));
        var validator = new CragValidator(client);
        var (results, query) = CreateResultsWithChunks(3);

        var output = await validator.ProcessAsync(results, query);

        Assert.Equal(3, output.Metadata["crag_score"]);
        Assert.Equal("Ambiguous", output.Metadata["crag_path"]);
    }

    [Fact]
    public async Task ProcessAsync_StoresReasoningInMetadata()
    {
        using var client = TestChatClient.WithJsonResponse("""{"score": 4, "reasoning": "relevant results"}""");
        var validator = new CragValidator(client);
        var (results, query) = CreateResultsWithChunks(3);

        var output = await validator.ProcessAsync(results, query);

        Assert.Equal("relevant results", output.Metadata["crag_reasoning"]);
    }

    private static (RetrievalResults results, RetrievalQuery query) CreateResultsWithChunks(int count)
    {
        var query = new RetrievalQuery("test query");
        var results = new RetrievalResults
        {
            Chunks = Enumerable.Range(0, count)
                .Select(i => new RetrievalChunk($"Chunk {i} content", 0.9 - i * 0.1))
                .ToList()
        };
        return (results, query);
    }
}
