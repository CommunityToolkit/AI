using Microsoft.Extensions.DataIngestion;
using CommunityToolkit.DataIngestion.UnitTests.Utils;

namespace CommunityToolkit.DataIngestion.UnitTests.Ingestion;

public class EntityExtractionProcessorTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient",
            () => new EntityExtractionProcessor(null!));
    }

    [Fact]
    public async Task ProcessAsync_ExtractsEntitiesIntoMetadata()
    {
        using var client = TestChatClient.WithJsonResponse("""
            {
                "people": ["John Doe", "Jane Smith"],
                "organizations": ["Microsoft", "OpenAI"],
                "technologies": [".NET", "Azure"],
                "versions": ["8.0", "10.0"]
            }
            """);
        var processor = new EntityExtractionProcessor(client);
        var chunks = CreateChunks("John Doe from Microsoft wrote about .NET 8.0");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Single(results);
        Assert.Equal("John Doe, Jane Smith", results[0].Metadata[MetadataKeys.EntitiesPeople]);
        Assert.Equal("Microsoft, OpenAI", results[0].Metadata[MetadataKeys.EntitiesOrganizations]);
        Assert.Equal(".NET, Azure", results[0].Metadata[MetadataKeys.EntitiesTechnologies]);
        Assert.Equal("8.0, 10.0", results[0].Metadata[MetadataKeys.EntitiesVersions]);
    }

    [Fact]
    public async Task ProcessAsync_LlmThrows_SetsEmptyDefaults()
    {
        using var client = TestChatClient.WithException(new InvalidOperationException("timeout"));
        var processor = new EntityExtractionProcessor(client);
        var chunks = CreateChunks("some text");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Single(results);
        Assert.Equal("", results[0].Metadata[MetadataKeys.EntitiesPeople]);
        Assert.Equal("", results[0].Metadata[MetadataKeys.EntitiesOrganizations]);
        Assert.Equal("", results[0].Metadata[MetadataKeys.EntitiesTechnologies]);
        Assert.Equal("", results[0].Metadata[MetadataKeys.EntitiesVersions]);
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
