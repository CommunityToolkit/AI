using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.Util;

namespace CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;

public sealed class PdfPigDocumentExtractionClientTests
{
    [Fact]
    public async Task ExtractAsync_NativeText_ReturnsExactTextBlockPolygon()
    {
        using var input = new MemoryStream(PdfFixture.Create());
        using var client = new PdfPigDocumentExtractionClient();
        ExtractionDocument result = await client.ExtractAsync(input, "invoice.pdf", "application/pdf");
        var text = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(result.EnumerateContent()));
        Assert.Equal("Native invoice 42", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Null(text.Role);
        var source = Assert.Single(text.Sources);
        var native = Assert.IsType<TextBlock>(source.GetRawRepresentation());
        Assert.Equal(native.Text, Assert.IsType<TextContent>(text.Content).Text);
        var bounds = native.BoundingBox;
        Assert.Equal(new[] { (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom),
            (bounds.Right, bounds.Top), (bounds.Left, bounds.Top) },
            source.Polygon.Select(point => (point.X, point.Y)));
        Assert.Same(result.SourcePages[0], source.Page);
        Assert.Null(result.RawRepresentation);
        Assert.Equal(1, source.Page.PageNumber);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, source.Page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, source.Page.CoordinateOrigin);
    }

    [Fact]
    public async Task ExtractAsync_MultiplePages_PreservesOriginalPageNumbersAndDimensions()
    {
        using var input = new MemoryStream(PdfFixture.Create());
        using var client = new PdfPigDocumentExtractionClient();
        var result = await client.ExtractAsync(input, "pages", "APPLICATION/PDF");
        Assert.Equal(new int?[] { 1, 2, 3 }, result.SourcePages.Select(page => page.PageNumber));
        Assert.Equal(new[] { (300d, 400d), (500d, 600d), (700d, 800d) },
            result.SourcePages.Select(page => (page.Dimensions!.Value.Width, page.Dimensions.Value.Height)));
        Assert.Equal(3, result.Sections.Count);
        Assert.Empty(result.Sections[1].Elements);
        Assert.Empty(result.Sections[2].Elements);
        Assert.Single(result.EnumerateContent());
    }

    [Fact]
    public async Task ExtractAsync_NullNativeRoot_PreservesIndependentTextBlockAfterClientAndInputDisposal()
    {
        using var input = new MemoryStream(PdfFixture.Create());
        using var client = new PdfPigDocumentExtractionClient();
        var result = await client.ExtractAsync(input, "native-root", "application/pdf");
        client.Dispose();
        input.Dispose();
        Assert.Null(result.RawRepresentation);
        var text = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(result.EnumerateContent()));
        var source = Assert.Single(text.Sources);
        var native = Assert.IsType<TextBlock>(source.GetRawRepresentation());
        Assert.Equal("Native invoice 42", native.Text);
        Assert.Equal(native.Text, Assert.IsType<TextContent>(text.Content).Text);
        Assert.Same(result.SourcePages[0], source.Page);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("malformed")]
    [InlineData("media")]
    [InlineData("cancelled")]
    [InlineData("mid-copy")]
    public async Task ExtractAsync_BorrowedStreamSuccessFailureCancellation_RemainsReadable(string scenario)
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CopyCancellationStream(scenario == "malformed" ? [1, 2, 3] : PdfFixture.Create(),
            scenario == "mid-copy" ? cancellation : null);
        var client = new PdfPigDocumentExtractionClient();
        if (scenario == "cancelled")
        {
            cancellation.Cancel();
        }

        Task<ExtractionDocument> operation = client.ExtractAsync(input, "borrowed",
            scenario == "media" ? "text/plain" : "application/pdf", cancellationToken: cancellation.Token);
        if (scenario == "success")
        {
            Assert.Single((await operation).EnumerateContent());
        }
        else if (scenario is "cancelled" or "mid-copy")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        else if (scenario == "media")
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => operation);
        }
        else
        {
            Assert.NotNull(await Record.ExceptionAsync(() => operation));
        }

        client.Dispose();
        client.Dispose();
        Assert.False(input.Disposed);
        input.Position = 0;
        Assert.Equal(scenario == "malformed" ? 1 : '%', input.ReadByte());
    }

    [Fact]
    public async Task Dispose_RepeatedCalls_DoesNotOwnInjectedDependencies()
    {
        var segmenter = new BorrowedSegmenter();
        var words = new BorrowedWordExtractor();
        var client = new PdfPigDocumentExtractionClient(segmenter, words, _ => "title");
        client.Dispose();
        client.Dispose();
        using var input = new MemoryStream(PdfFixture.Create());
        var result = await client.ExtractAsync(input, "injected", "application/pdf");
        var text = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(result.EnumerateContent()));
        Assert.Equal("heading", text.Role);
        Assert.Equal("Native invoice 42", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Equal(3, segmenter.Calls);
        Assert.True(words.Calls > 0);
        Assert.False(segmenter.Disposed);
        Assert.False(words.Disposed);
    }

    [Fact]
    public void GetService_SelfUnknownTypeAndServiceKey_ReturnsOnlySupportedService()
    {
        using var client = new PdfPigDocumentExtractionClient();
        Assert.Same(client, client.GetService(typeof(PdfPigDocumentExtractionClient)));
        Assert.Same(client, client.GetService(typeof(IDocumentExtractionClient)));
        Assert.Null(client.GetService(typeof(Stream)));
        Assert.Null(client.GetService(typeof(IDocumentExtractionClient), "key"));
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
    }

    [Theory]
    [InlineData("identifier")]
    [InlineData("mediaType")]
    [InlineData("stream")]
    [InlineData("property")]
    [InlineData("extractor")]
    public async Task ExtractAsync_InvalidInput_RejectsBeforeReadingBorrowedStream(string guard)
    {
        using var input = new MemoryStream(PdfFixture.Create());
        using var client = new PdfPigDocumentExtractionClient();
        var options = new DocumentExtractionOptions();
        if (guard == "property")
        {
            options.AdditionalProperties["unsupported"] = true;
        }
        if (guard == "extractor")
        {
            options.ExtractorName = "other";
        }

        Exception? error = await Record.ExceptionAsync(() => client.ExtractAsync(guard == "stream" ? null! : input,
            guard == "identifier" ? "" : "id", guard == "mediaType" ? "" : "application/pdf", options));
        if (guard is "property" or "extractor")
        {
            Assert.IsType<NotSupportedException>(error);
        }
        else if (guard == "stream")
        {
            Assert.Equal("document", Assert.IsType<ArgumentNullException>(error).ParamName);
        }
        else
        {
            Assert.Equal(guard, Assert.IsType<ArgumentException>(error).ParamName);
        }
        Assert.Equal(0, input.Position);
        Assert.Equal('%', input.ReadByte());
    }

    private sealed class CopyCancellationStream(byte[] bytes, CancellationTokenSource? cancellation) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            cancellation?.Cancel();
            return base.CopyToAsync(destination, bufferSize, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BorrowedSegmenter : IPageSegmenter, IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public IReadOnlyList<TextBlock> GetBlocks(IEnumerable<UglyToad.PdfPig.Content.Word> words)
        {
            Calls++;
            return DefaultPageSegmenter.Instance.GetBlocks(words);
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class BorrowedWordExtractor : IWordExtractor, IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public IEnumerable<UglyToad.PdfPig.Content.Word> GetWords(IReadOnlyList<UglyToad.PdfPig.Content.Letter> letters)
        {
            Calls++;
            return DefaultWordExtractor.Instance.GetWords(letters);
        }
        public void Dispose() => Disposed = true;
    }
}
