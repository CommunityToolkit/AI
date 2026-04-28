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

public class VisionTableEnricherTests
{
    private class TestChatClient : IChatClient
    {
        private readonly string _response;

        public List<ChatMessage> LastMessages { get; private set; } = new();

        public TestChatClient(string response) => _response = response;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastMessages = messages.ToList();
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
        Assert.Throws<ArgumentNullException>(() => new VisionTableEnricher(null!));
    }

    [Fact]
    public async Task ProcessAsync_TableElement_GetsEnrichedWithMarkdown()
    {
        var expectedMarkdown = "| H1 | H2 |\n|---|---|\n| V1 | V2 |";
        var client = new TestChatClient(expectedMarkdown);
        var enricher = new VisionTableEnricher(client);

        var doc = CreateDocumentWithTable();

        var result = await enricher.ProcessAsync(doc);

        var table = result.EnumerateContent().OfType<IngestionDocumentTable>().First();
        Assert.True(table.HasMetadata);
        Assert.True(table.Metadata.ContainsKey("enriched_markdown_table"));
        Assert.Equal(expectedMarkdown, table.Metadata["enriched_markdown_table"]);
    }

    [Fact]
    public async Task ProcessAsync_NonTableElement_NotModified()
    {
        var client = new TestChatClient("should not appear");
        var enricher = new VisionTableEnricher(client);

        var doc = CreateDocumentWithTable();

        var result = await enricher.ProcessAsync(doc);

        var paragraphs = result.EnumerateContent()
            .OfType<IngestionDocumentParagraph>()
            .ToList();

        foreach (var p in paragraphs)
        {
            Assert.False(p.Metadata.ContainsKey("enriched_markdown_table"));
        }
    }

    [Fact]
    public async Task ProcessAsync_EmptyDocument_NoException()
    {
        var client = new TestChatClient("response");
        var enricher = new VisionTableEnricher(client);

        var doc = new IngestionDocument("empty.pdf");

        var result = await enricher.ProcessAsync(doc);

        Assert.NotNull(result);
        Assert.Empty(result.Sections);
    }

    [Fact]
    public async Task ProcessAsync_DocumentWithNoTables_NoEnrichment()
    {
        var client = new TestChatClient("response");
        var enricher = new VisionTableEnricher(client);

        var doc = new IngestionDocument("no-tables.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        section.Elements.Add(new IngestionDocumentParagraph("Regular text") { Text = "Regular text" });
        doc.Sections.Add(section);

        var result = await enricher.ProcessAsync(doc);

        var elements = result.EnumerateContent().ToList();
        foreach (var e in elements)
        {
            Assert.False(e.Metadata.ContainsKey("enriched_markdown_table"));
        }
    }

    [Fact]
    public async Task ProcessAsync_MultipleTables_AllEnriched()
    {
        var expectedMarkdown = "| A | B |";
        var client = new TestChatClient(expectedMarkdown);
        var enricher = new VisionTableEnricher(client);

        var doc = new IngestionDocument("multi-table.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };

        section.Elements.Add(CreateTable("| T1C1 | T1C2 |"));
        section.Elements.Add(CreateTable("| T2C1 | T2C2 |"));
        doc.Sections.Add(section);

        var result = await enricher.ProcessAsync(doc);

        var tables = result.EnumerateContent().OfType<IngestionDocumentTable>().ToList();
        Assert.Equal(2, tables.Count);
        Assert.All(tables, t =>
        {
            Assert.True(t.Metadata.ContainsKey("enriched_markdown_table"));
            Assert.Equal(expectedMarkdown, t.Metadata["enriched_markdown_table"]);
        });
    }

    [Fact]
    public async Task ProcessAsync_CancellationRequested_Throws()
    {
        var client = new TestChatClient("response");
        var enricher = new VisionTableEnricher(client);

        var doc = CreateDocumentWithTable();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => enricher.ProcessAsync(doc, cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSameDocumentInstance()
    {
        var client = new TestChatClient("markdown");
        var enricher = new VisionTableEnricher(client);

        var doc = CreateDocumentWithTable();

        var result = await enricher.ProcessAsync(doc);

        Assert.Same(doc, result);
    }

    [Fact]
    public async Task ProcessAsync_WithPageImage_SendsDataContentToLlm()
    {
        var expectedMarkdown = "| A | B |";
        var client = new TestChatClient(expectedMarkdown);
        var enricher = new VisionTableEnricher(client);

        var fakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        section.Metadata["page_image"] = fakeImageBytes;

        var table = CreateTable("| raw | data |");
        table.PageNumber = 1;
        section.Elements.Add(table);
        doc.Sections.Add(section);

        await enricher.ProcessAsync(doc);

        var userMsg = client.LastMessages.Last(m => m.Role == ChatRole.User);
        Assert.Contains(userMsg.Contents, c => c is DataContent dc && dc.MediaType == "image/png");
    }

    [Fact]
    public async Task ProcessAsync_WithoutPageImage_SendsTextOnlyToLlm()
    {
        var expectedMarkdown = "| A | B |";
        var client = new TestChatClient(expectedMarkdown);
        var enricher = new VisionTableEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        // No page_image metadata

        var table = CreateTable("| raw | data |");
        table.PageNumber = 1;
        section.Elements.Add(table);
        doc.Sections.Add(section);

        await enricher.ProcessAsync(doc);

        var userMsg = client.LastMessages.Last(m => m.Role == ChatRole.User);
        Assert.DoesNotContain(userMsg.Contents, c => c is DataContent);
    }

    private static IngestionDocument CreateDocumentWithTable()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };

        section.Elements.Add(CreateTable("| H1 | H2 |\n|---|---|\n| V1 | V2 |"));

        var paragraph = new IngestionDocumentParagraph("Regular text") { Text = "Regular text" };
        section.Elements.Add(paragraph);

        doc.Sections.Add(section);
        return doc;
    }

    private static IngestionDocumentTable CreateTable(string markdown)
    {
        var cells = new IngestionDocumentElement[1, 2];
        cells[0, 0] = new IngestionDocumentParagraph("C1") { Text = "C1" };
        cells[0, 1] = new IngestionDocumentParagraph("C2") { Text = "C2" };
        return new IngestionDocumentTable(markdown, cells);
    }
}
#endif
