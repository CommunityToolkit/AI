using Microsoft.Extensions.DataIngestion;
using CommunityToolkit.DataIngestion.UnitTests.Utils;

namespace CommunityToolkit.DataIngestion.UnitTests.Ingestion;

public class HypotheticalQueryProcessorTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient",
            () => new HypotheticalQueryProcessor(null!));
    }

    [Fact]
    public async Task ProcessAsync_GeneratesQuestionsAndYieldsOriginalFirst()
    {
        using var client = TestChatClient.WithJsonResponse(
            """{"questions": ["What is dependency injection?", "How to register services?", "What is IServiceProvider?"]}""");
        var processor = new HypotheticalQueryProcessor(client, questionsPerChunk: 3);
        var chunks = CreateChunks("DI in .NET uses IServiceCollection.");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        // Original chunk first, then hypothetical query chunks
        Assert.True(results.Count >= 2);
        Assert.Equal(MetadataKeys.ChunkTypeOriginal, results[0].Metadata[MetadataKeys.ChunkType]);

        var queryChunks = results.Where(r =>
            (string)r.Metadata[MetadataKeys.ChunkType]! == MetadataKeys.ChunkTypeHypotheticalQuery).ToList();
        Assert.True(queryChunks.Count > 0);
        Assert.All(queryChunks, q => Assert.NotEmpty(q.Content));
    }

    [Fact]
    public async Task ProcessAsync_LlmThrows_YieldsOriginalOnly()
    {
        using var client = TestChatClient.WithException(new InvalidOperationException("timeout"));
        var processor = new HypotheticalQueryProcessor(client);
        var chunks = CreateChunks("some content");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Single(results);
        Assert.Equal(MetadataKeys.ChunkTypeOriginal, results[0].Metadata[MetadataKeys.ChunkType]);
        Assert.Equal("some content", results[0].Content);
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
