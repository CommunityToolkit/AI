using Microsoft.Extensions.DataIngestion;
using CommunityToolkit.DataIngestion.UnitTests.Utils;

namespace CommunityToolkit.DataIngestion.UnitTests.Ingestion;

public class TreeIndexProcessorTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient",
            () => new TreeIndexProcessor(null!));
    }

    [Fact]
    public async Task ProcessAsync_MarksLeafChunksWithLevel0()
    {
        using var client = TestChatClient.WithJsonResponse("Branch summary text");
        var processor = new TreeIndexProcessor(client);
        var chunks = CreateChunks("Chunk 1 content", "Chunk 2 content");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        var leaves = results.Where(r => (int)r.Metadata[MetadataKeys.Level]! == 0).ToList();
        Assert.Equal(2, leaves.Count);
        Assert.All(leaves, l => Assert.Equal(MetadataKeys.ChunkTypeOriginal, l.Metadata[MetadataKeys.ChunkType]));
    }

    [Fact]
    public async Task ProcessAsync_GeneratesBranchAndRootSummaries()
    {
        int callCount = 0;
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                callCount++;
                var text = callCount == 1
                    ? "Branch summary of the document."
                    : "Root summary of the entire corpus.";
                return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant, text)));
            }
        };
        var processor = new TreeIndexProcessor(client);
        var chunks = CreateChunks("Chunk 1", "Chunk 2", "Chunk 3");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        // Should have leaf chunks + at least 1 branch + 1 root summary
        var branches = results.Where(r =>
            r.Metadata.ContainsKey(MetadataKeys.ChunkType) &&
            (string)r.Metadata[MetadataKeys.ChunkType]! == MetadataKeys.ChunkTypeBranchSummary).ToList();
        var roots = results.Where(r =>
            r.Metadata.ContainsKey(MetadataKeys.ChunkType) &&
            (string)r.Metadata[MetadataKeys.ChunkType]! == MetadataKeys.ChunkTypeRootSummary).ToList();

        Assert.True(branches.Count >= 1, "Should generate at least one branch summary");
        Assert.True(roots.Count >= 1, "Should generate at least one root summary");

        // Branch = level 1, Root = level 2
        Assert.All(branches, b => Assert.Equal(1, b.Metadata[MetadataKeys.Level]));
        Assert.All(roots, r => Assert.Equal(2, r.Metadata[MetadataKeys.Level]));
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
