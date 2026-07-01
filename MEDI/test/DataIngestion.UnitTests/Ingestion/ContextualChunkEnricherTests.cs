using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using CommunityToolkit.DataIngestion.UnitTests.Utils;

namespace CommunityToolkit.DataIngestion.UnitTests.Ingestion;

public class ContextualChunkEnricherTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient",
            () => new ContextualChunkEnricher(null!));
    }

    [Fact]
    public async Task ProcessAsync_AddsContextualSummaryMetadata()
    {
        var summaryText = "This chunk discusses PDF text extraction.";
        using var client = CreateClientReturning(summaryText);
        var processor = new ContextualChunkEnricher(client);
        var chunks = CreateChunks("Some text about PDF extraction and analysis.");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Single(results);
        Assert.True(results[0].Metadata.ContainsKey(MetadataKeys.ContextualSummary));
        Assert.Equal(summaryText, results[0].Metadata[MetadataKeys.ContextualSummary]);
    }

    [Fact]
    public async Task ProcessAsync_MultipleChunks_AllGetSummaries()
    {
        var summaryText = "Summary text.";
        using var client = CreateClientReturning(summaryText);
        var processor = new ContextualChunkEnricher(client);
        var chunks = CreateChunks("First chunk content.", "Second chunk content.");

        var results = await CollectAsync(processor.ProcessAsync(chunks));

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.True(r.Metadata.ContainsKey(MetadataKeys.ContextualSummary));
            Assert.Equal(summaryText, r.Metadata[MetadataKeys.ContextualSummary]);
        });
    }

    [Fact]
    public async Task ProcessAsync_EmptyChunks_NoResults()
    {
        using var client = CreateClientReturning("summary");
        var processor = new ContextualChunkEnricher(client);

        var results = await CollectAsync(processor.ProcessAsync(CreateChunks()));

        Assert.Empty(results);
    }

    [Fact]
    public async Task ProcessAsync_CancellationRequested_Throws()
    {
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
            }
        };
        var processor = new ContextualChunkEnricher(client);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in processor.ProcessAsync(CreateChunks("Some content."), cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task ProcessAsync_PreservesChunkContentDocumentAndInstance()
    {
        using var client = CreateClientReturning("A summary.");
        var processor = new ContextualChunkEnricher(client);
        var document = new IngestionDocument("test-doc");
        var originalContent = "Original chunk content stays the same.";
        var chunk = new IngestionChunk<string>(originalContent, document);

        var results = await CollectAsync(processor.ProcessAsync(CreateChunkInstances(chunk)));

        var result = Assert.Single(results);
        Assert.Same(chunk, result);
        Assert.Same(document, result.Document);
        Assert.Equal(originalContent, result.Content);
    }

    [Theory]
    [InlineData("table", "Summarize this table for search retrieval. Describe what data it contains, including key metrics, column headers, and notable values. Output only the summary sentence, nothing else.")]
    [InlineData("picture", "Summarize what this figure or image refers to for search retrieval. Output only the summary sentence, nothing else.")]
    [InlineData("caption", "Summarize what this figure or image refers to for search retrieval. Output only the summary sentence, nothing else.")]
    [InlineData(null, "Provide a single concise sentence summarizing the following text for use in search retrieval. Output only the summary sentence, nothing else.")]
    public async Task ProcessAsync_UsesElementTypeSpecificPrompt(string? elementType, string expectedInstruction)
    {
        string? prompt = null;
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, _, _) =>
            {
                prompt = Assert.Single(messages).Text;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
            }
        };
        var processor = new ContextualChunkEnricher(client);
        var document = new IngestionDocument("test-doc");
        var chunk = new IngestionChunk<string>("Chunk content.", document);
        if (elementType is not null)
            chunk.Metadata["element_type"] = elementType;

        await CollectAsync(processor.ProcessAsync(CreateChunkInstances(chunk)));

        Assert.Equal(expectedInstruction + "\n\nChunk content.", prompt);
    }

    private static TestChatClient CreateClientReturning(string response)
        => new()
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)))
        };

    private static async IAsyncEnumerable<IngestionChunk<string>> CreateChunks(params string[] contents)
    {
        await Task.CompletedTask;
        var doc = new IngestionDocument("test-doc");
        foreach (var content in contents)
            yield return new IngestionChunk<string>(content, doc);
    }

    private static async IAsyncEnumerable<IngestionChunk<string>> CreateChunkInstances(params IngestionChunk<string>[] chunks)
    {
        await Task.CompletedTask;
        foreach (var chunk in chunks)
            yield return chunk;
    }

    private static async Task<List<IngestionChunk<string>>> CollectAsync(IAsyncEnumerable<IngestionChunk<string>> source)
    {
        var list = new List<IngestionChunk<string>>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
