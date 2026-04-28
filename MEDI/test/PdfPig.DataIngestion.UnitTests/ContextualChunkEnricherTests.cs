#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.Processors;
using Xunit;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

public class ContextualChunkEnricherTests
{
    private class TestChatClient : IChatClient
    {
        private readonly string _response;

        public TestChatClient(string response) => _response = response;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var msg = new ChatMessage(ChatRole.Assistant, _response);
            return Task.FromResult(new ChatResponse(msg));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public void Constructor_NullChatClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ContextualChunkEnricher(null!));
    }

    [Fact]
    public async Task ProcessAsync_AddsContextualSummaryMetadata()
    {
        var summaryText = "This chunk discusses PDF text extraction.";
        var client = new TestChatClient(summaryText);
        var enricher = new ContextualChunkEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var chunk = new IngestionChunk<string>("Some text about PDF extraction and analysis.", doc, "Page 1");

        var results = new List<IngestionChunk<string>>();
        await foreach (var c in enricher.ProcessAsync(ToAsyncEnumerable(chunk)))
        {
            results.Add(c);
        }

        Assert.Single(results);
        Assert.True(results[0].Metadata.ContainsKey("contextual_summary"));
        Assert.Equal(summaryText, results[0].Metadata["contextual_summary"]);
    }

    [Fact]
    public async Task ProcessAsync_MultipleChunks_AllGetSummaries()
    {
        var summaryText = "Summary text.";
        var client = new TestChatClient(summaryText);
        var enricher = new ContextualChunkEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var chunk1 = new IngestionChunk<string>("First chunk content.", doc, "Page 1");
        var chunk2 = new IngestionChunk<string>("Second chunk content.", doc, "Page 2");

        var results = new List<IngestionChunk<string>>();
        await foreach (var c in enricher.ProcessAsync(ToAsyncEnumerable(chunk1, chunk2)))
        {
            results.Add(c);
        }

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.True(r.Metadata.ContainsKey("contextual_summary"));
            Assert.Equal(summaryText, r.Metadata["contextual_summary"]);
        });
    }

    [Fact]
    public async Task ProcessAsync_EmptyChunks_NoResults()
    {
        var client = new TestChatClient("summary");
        var enricher = new ContextualChunkEnricher(client);

        var results = new List<IngestionChunk<string>>();
        await foreach (var c in enricher.ProcessAsync(ToAsyncEnumerable<IngestionChunk<string>>()))
        {
            results.Add(c);
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task ProcessAsync_CancellationRequested_Throws()
    {
        var client = new TestChatClient("summary");
        var enricher = new ContextualChunkEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var chunk = new IngestionChunk<string>("Some content.", doc, "Page 1");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in enricher.ProcessAsync(ToAsyncEnumerable(chunk), cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task ProcessAsync_PreservesChunkContent()
    {
        var client = new TestChatClient("A summary.");
        var enricher = new ContextualChunkEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var originalContent = "Original chunk content stays the same.";
        var chunk = new IngestionChunk<string>(originalContent, doc, "Page 1");

        var results = new List<IngestionChunk<string>>();
        await foreach (var c in enricher.ProcessAsync(ToAsyncEnumerable(chunk)))
        {
            results.Add(c);
        }

        Assert.Single(results);
        Assert.Equal(originalContent, results[0].Content);
    }

    [Fact]
    public async Task ProcessAsync_PreservesChunkDocumentReference()
    {
        var client = new TestChatClient("A summary.");
        var enricher = new ContextualChunkEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var chunk = new IngestionChunk<string>("Content.", doc, "Page 1");

        var results = new List<IngestionChunk<string>>();
        await foreach (var c in enricher.ProcessAsync(ToAsyncEnumerable(chunk)))
        {
            results.Add(c);
        }

        Assert.Single(results);
        Assert.Same(doc, results[0].Document);
    }

    [Fact]
    public async Task ProcessAsync_YieldsSameChunkInstances()
    {
        var client = new TestChatClient("Summary.");
        var enricher = new ContextualChunkEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var chunk = new IngestionChunk<string>("Content.", doc, "Page 1");

        var results = new List<IngestionChunk<string>>();
        await foreach (var c in enricher.ProcessAsync(ToAsyncEnumerable(chunk)))
        {
            results.Add(c);
        }

        Assert.Single(results);
        Assert.Same(chunk, results[0]);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
#endif
