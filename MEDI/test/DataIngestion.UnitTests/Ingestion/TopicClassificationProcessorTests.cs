using Microsoft.Extensions.DataIngestion;
using CommunityToolkit.DataIngestion.UnitTests.Utils;

namespace CommunityToolkit.DataIngestion.UnitTests.Ingestion;

public class TopicClassificationProcessorTests
{
    private static readonly string[] Taxonomy = ["web", "data", "security", "performance", "architecture"];

    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient",
            () => new TopicClassificationProcessor(null!, Taxonomy));
    }

    [Fact]
    public void Constructor_ThrowsOnNullTaxonomy()
    {
        using var client = TestChatClient.WithJsonResponse("{}");
        Assert.Throws<ArgumentNullException>("taxonomy",
            () => new TopicClassificationProcessor(client, null!));
    }

    [Fact]
    public async Task ProcessAsync_ValidTopic_SetsMetadata()
    {
        using var client = TestChatClient.WithJsonResponse(
            """{"primary": "security", "secondary": ["architecture", "web"]}""");
        var processor = new TopicClassificationProcessor(client, Taxonomy);
        var chunks = CreateChunks("OAuth2 tokens need proper architecture");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Single(results);
        Assert.Equal("security", results[0].Metadata[MetadataKeys.TopicPrimary]);
        Assert.Contains("architecture", (string)results[0].Metadata[MetadataKeys.TopicSecondary]!);
    }

    [Fact]
    public async Task ProcessAsync_InvalidPrimary_DefaultsToUnknown()
    {
        using var client = TestChatClient.WithJsonResponse(
            """{"primary": "not_in_taxonomy", "secondary": []}""");
        var processor = new TopicClassificationProcessor(client, Taxonomy);
        var chunks = CreateChunks("some random text");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Single(results);
        Assert.Equal("unknown", results[0].Metadata[MetadataKeys.TopicPrimary]);
    }

    [Fact]
    public async Task ProcessAsync_LlmThrows_DefaultsToUnknown()
    {
        using var client = TestChatClient.WithException(new InvalidOperationException("fail"));
        var processor = new TopicClassificationProcessor(client, Taxonomy);
        var chunks = CreateChunks("text");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Single(results);
        Assert.Equal("unknown", results[0].Metadata[MetadataKeys.TopicPrimary]);
        Assert.Equal("", results[0].Metadata[MetadataKeys.TopicSecondary]);
    }

    private static async IAsyncEnumerable<IngestionChunk<string>> CreateChunks(params string[] contents)
    {
        await Task.CompletedTask;
        var doc = new IngestionDocument("test-doc");
        foreach (var content in contents)
            yield return new IngestionChunk<string>(content, doc);
    }

    private static async Task<List<IngestionChunk<string>>> CollectAsync(IAsyncEnumerable<IngestionChunk<string>> source)
    {
        var list = new List<IngestionChunk<string>>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
