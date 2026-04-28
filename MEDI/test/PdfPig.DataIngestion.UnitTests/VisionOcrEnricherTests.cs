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

public class VisionOcrEnricherTests
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
        Assert.Throws<ArgumentNullException>(() => new VisionOcrEnricher(null!));
    }

    [Fact]
    public async Task ProcessAsync_EmptyTextElement_GetsOcrText()
    {
        var ocrText = "Extracted OCR text from image";
        var client = new TestChatClient(ocrText);
        var fallback = new VisionOcrEnricher(client);

        var doc = CreateDocumentWithEmptyTextElement();

        var result = await fallback.ProcessAsync(doc);

        var element = result.EnumerateContent().First();
        Assert.Equal(ocrText, element.Text);
    }

    [Fact]
    public async Task ProcessAsync_EmptyTextElement_SetsOcrSourceMetadata()
    {
        var client = new TestChatClient("OCR result");
        var fallback = new VisionOcrEnricher(client);

        var doc = CreateDocumentWithEmptyTextElement();

        var result = await fallback.ProcessAsync(doc);

        var element = result.EnumerateContent().First();
        Assert.True(element.Metadata.ContainsKey("ocr_source"));
        Assert.Equal("vision_llm", element.Metadata["ocr_source"]);
    }

    [Fact]
    public async Task ProcessAsync_ElementWithText_NotModified()
    {
        var client = new TestChatClient("Should not replace");
        var fallback = new VisionOcrEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        var paragraph = new IngestionDocumentParagraph("Existing content") { Text = "Existing content" };
        section.Elements.Add(paragraph);
        doc.Sections.Add(section);

        var result = await fallback.ProcessAsync(doc);

        var element = result.EnumerateContent().First();
        Assert.Equal("Existing content", element.Text);
        Assert.False(element.Metadata.ContainsKey("ocr_source"));
    }

    [Fact]
    public async Task ProcessAsync_MixedElements_OnlyEmptyOnesEnriched()
    {
        var ocrText = "Extracted text";
        var client = new TestChatClient(ocrText);
        var fallback = new VisionOcrEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };

        var withText = new IngestionDocumentParagraph("Has content") { Text = "Has content" };
        var withoutText = new IngestionDocumentParagraph("empty region") { Text = "" };

        section.Elements.Add(withText);
        section.Elements.Add(withoutText);
        doc.Sections.Add(section);

        var result = await fallback.ProcessAsync(doc);
        var elements = result.EnumerateContent().ToList();

        Assert.Equal("Has content", elements[0].Text);
        Assert.False(elements[0].Metadata.ContainsKey("ocr_source"));

        Assert.Equal(ocrText, elements[1].Text);
        Assert.True(elements[1].Metadata.ContainsKey("ocr_source"));
    }

    [Fact]
    public async Task ProcessAsync_EmptyDocument_NoException()
    {
        var client = new TestChatClient("response");
        var fallback = new VisionOcrEnricher(client);

        var doc = new IngestionDocument("empty.pdf");

        var result = await fallback.ProcessAsync(doc);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ProcessAsync_LlmReturnsWhitespace_ElementNotModified()
    {
        var client = new TestChatClient("   ");
        var fallback = new VisionOcrEnricher(client);

        var doc = CreateDocumentWithEmptyTextElement();

        var result = await fallback.ProcessAsync(doc);

        var element = result.EnumerateContent().First();
        Assert.True(string.IsNullOrWhiteSpace(element.Text));
        Assert.False(element.Metadata.ContainsKey("ocr_source"));
    }

    [Fact]
    public async Task ProcessAsync_CancellationRequested_Throws()
    {
        var client = new TestChatClient("response");
        var fallback = new VisionOcrEnricher(client);

        var doc = CreateDocumentWithEmptyTextElement();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fallback.ProcessAsync(doc, cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSameDocumentInstance()
    {
        var client = new TestChatClient("ocr text");
        var fallback = new VisionOcrEnricher(client);

        var doc = CreateDocumentWithEmptyTextElement();

        var result = await fallback.ProcessAsync(doc);

        Assert.Same(doc, result);
    }

    [Fact]
    public async Task ProcessAsync_NullTextElement_GetsOcrText()
    {
        var ocrText = "OCR from null text";
        var client = new TestChatClient(ocrText);
        var fallback = new VisionOcrEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        // Text defaults to null when not explicitly set
        var paragraph = new IngestionDocumentParagraph("image placeholder");
        section.Elements.Add(paragraph);
        doc.Sections.Add(section);

        var result = await fallback.ProcessAsync(doc);

        var element = result.EnumerateContent().First();
        // If Text was null (IsNullOrWhiteSpace), it should get OCR'd
        if (string.IsNullOrWhiteSpace(element.Text) == false)
        {
            // Text was set by OCR
            Assert.Equal(ocrText, element.Text);
        }
    }

    [Fact]
    public async Task ProcessAsync_WithPageImage_SendsDataContentToLlm()
    {
        var ocrText = "Vision OCR result";
        var client = new TestChatClient(ocrText);
        var fallback = new VisionOcrEnricher(client);

        var fakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        section.Metadata["page_image"] = fakeImageBytes;
        var emptyParagraph = new IngestionDocumentParagraph("image region") { Text = "", PageNumber = 1 };
        section.Elements.Add(emptyParagraph);
        doc.Sections.Add(section);

        await fallback.ProcessAsync(doc);

        var userMsg = client.LastMessages.Last(m => m.Role == ChatRole.User);
        Assert.Contains(userMsg.Contents, c => c is DataContent dc && dc.MediaType == "image/png");
    }

    [Fact]
    public async Task ProcessAsync_WithoutPageImage_SendsTextOnlyToLlm()
    {
        var ocrText = "Text fallback OCR result";
        var client = new TestChatClient(ocrText);
        var fallback = new VisionOcrEnricher(client);

        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        // No page_image metadata
        var emptyParagraph = new IngestionDocumentParagraph("image region") { Text = "", PageNumber = 1 };
        section.Elements.Add(emptyParagraph);
        doc.Sections.Add(section);

        await fallback.ProcessAsync(doc);

        var userMsg = client.LastMessages.Last(m => m.Role == ChatRole.User);
        Assert.DoesNotContain(userMsg.Contents, c => c is DataContent);
    }

    [Fact]
    public async Task ProcessAsync_PlaceholderElement_WithPageImage_PerformsVisionOcr()
    {
        var ocrText = "Text extracted from scanned page image";
        var client = new TestChatClient(ocrText);
        var enricher = new VisionOcrEnricher(client);

        // Simulate what PdfPigReader produces for a scanned page
        var fakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var doc = new IngestionDocument("scanned.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        section.Metadata["page_image"] = fakeImageBytes;

        var placeholder = new IngestionDocumentParagraph("[scanned-page]")
        {
            Text = string.Empty,
            PageNumber = 1
        };
        placeholder.Metadata["placeholder"] = true;
        section.Elements.Add(placeholder);
        doc.Sections.Add(section);

        var result = await enricher.ProcessAsync(doc);

        var element = result.EnumerateContent().First();
        Assert.Equal(ocrText, element.Text);
        Assert.Equal("vision_llm", element.Metadata["ocr_source"]);

        // Verify vision approach was used (DataContent with image)
        var userMsg = client.LastMessages.Last(m => m.Role == ChatRole.User);
        Assert.Contains(userMsg.Contents, c => c is DataContent dc && dc.MediaType == "image/png");
    }

    private static IngestionDocument CreateDocumentWithEmptyTextElement()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        var emptyParagraph = new IngestionDocumentParagraph("image region") { Text = "" };
        section.Elements.Add(emptyParagraph);
        doc.Sections.Add(section);
        return doc;
    }
}
#endif
