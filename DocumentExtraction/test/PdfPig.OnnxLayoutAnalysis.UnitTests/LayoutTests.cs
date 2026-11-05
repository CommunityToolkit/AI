// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.ML.OnnxRuntime;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class LayoutTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> RasterCases()
    {
        foreach (int rotation in new[] { 0, 90, 180, 270 })
        {
            foreach (bool crop in new[] { false, true })
            {
                foreach (int dpi in new[] { 72, 96, 144, 216 })
                {
                    yield return [rotation, crop, dpi];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(RasterCases))]
    public void ActualRasterAndWordCoordinatesAgree(int rotation, bool crop, int dpi)
    {
        byte[] bytes = LayoutPdfFixture.Create(rotation, crop);
        using var pdf = PdfDocument.Open(bytes);
        var page = pdf.GetPage(1);
        using var reader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(dpi / 72d));
        using var nativePage = reader.GetPageReader(0);
        var raster = PageRaster.Render(nativePage, page, 32_000_000, default);
        var redPixels = new List<(int X, int Y)>();
        for (int y = 0; y < raster.Height; y++)
        {
            for (int x = 0; x < raster.Width; x++)
            {
                int index = (y * raster.Width + x) * 4;
                if (raster.Bgra[index + 2] > 240 && raster.Bgra[index + 1] < 15 &&
                    raster.Bgra[index] < 15 && raster.Bgra[index + 3] > 240)
                {
                    redPixels.Add((x, y));
                }
            }
        }

        Assert.NotEmpty(redPixels);
        var mapped = raster.ToPageBounds(
            redPixels.Min(p => p.X), redPixels.Min(p => p.Y),
            redPixels.Max(p => p.X) + 1, redPixels.Max(p => p.Y) + 1);
        PdfRectangle expected = rotation switch
        {
            0 => new(20, 430, 160, 500),
            90 => new(430, 240, 500, 380),
            180 => new(240, 50, 380, 120),
            270 => new(50, 20, 120, 160),
            _ => throw new InvalidOperationException(),
        };
        Assert.InRange(Math.Abs(mapped.Left - expected.Left), 0, 1.5);
        Assert.InRange(Math.Abs(mapped.Right - expected.Right), 0, 1.5);
        Assert.InRange(Math.Abs(mapped.Bottom - expected.Bottom), 0, 1.5);
        Assert.InRange(Math.Abs(mapped.Top - expected.Top), 0, 1.5);
        int inkWords = 0;
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text) &&
                word.BoundingBox.Centroid.X >= 0 && word.BoundingBox.Centroid.X <= page.Width &&
                word.BoundingBox.Centroid.Y >= 0 && word.BoundingBox.Centroid.Y <= page.Height).ToArray();
        foreach (var word in words)
        {
            var box = LayoutAssembler.AxisAligned(word.BoundingBox);
            int x0 = Math.Clamp((int)(box.Left * raster.Width / page.Width), 0, raster.Width - 1);
            int x1 = Math.Clamp((int)Math.Ceiling(box.Right * raster.Width / page.Width), 0, raster.Width);
            int y0 = Math.Clamp((int)((page.Height - box.Top) * raster.Height / page.Height), 0, raster.Height - 1);
            int y1 = Math.Clamp((int)Math.Ceiling((page.Height - box.Bottom) * raster.Height / page.Height), 0, raster.Height);
            bool ink = false;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int index = (y * raster.Width + x) * 4;
                    ink |= raster.Bgra[index + 3] > 80 && raster.Bgra[index + 2] < 120 &&
                        raster.Bgra[index + 1] < 120 && raster.Bgra[index] < 120;
                }
            }

            if (ink)
            {
                inkWords++;
            }
        }

        Assert.Equal(words.Length, inkWords);
        var tensor = raster.ToTensor(default);
        Assert.Equal(255, tensor[0, 0, 639, 639]);
        Assert.Equal(255, tensor[0, 1, 639, 639]);
        Assert.Equal(255, tensor[0, 2, 639, 639]);
        Assert.Contains(tensor, value => value < 100);
        int redX = (redPixels.Min(p => p.X) + redPixels.Max(p => p.X)) * 320 / raster.Width;
        int redY = (redPixels.Min(p => p.Y) + redPixels.Max(p => p.Y)) * 320 / raster.Height;
        Assert.Equal(255, tensor[0, 0, redY, redX]);
        Assert.Equal(0, tensor[0, 1, redY, redX]);
        Assert.Equal(0, tensor[0, 2, redY, redX]);
        PdfRectangle blueBox = rotation switch
        {
            0 => new(250, 430, 350, 500),
            90 => new(430, 50, 500, 150),
            180 => new(50, 50, 150, 120),
            270 => new(50, 250, 120, 350),
            _ => throw new InvalidOperationException(),
        };
        int blueX = (int)(blueBox.Centroid.X * 640 / page.Width);
        int blueY = (int)((page.Height - blueBox.Centroid.Y) * 640 / page.Height);
        Assert.Equal(0, tensor[0, 0, blueY, blueX]);
        Assert.Equal(0, tensor[0, 1, blueY, blueX]);
        Assert.Equal(255, tensor[0, 2, blueY, blueX]);
        output.WriteLine($"rotate={rotation}, crop={crop}, dpi={dpi}, raster={raster.Width}x{raster.Height}, words={inkWords}, red={mapped}");
    }

    [Fact]
    public void AssignsWordsOncePreservesMissesAndMapsRoles()
    {
        using var pdf = PdfDocument.Open(LayoutPdfFixture.Create());
        var page = pdf.GetPage(1);
        HeronDetection[] detections =
        [
            new("title", .9f, new PdfRectangle(20, 370, 380, 410)),
            new("text", .5f, new PdfRectangle(20, 370, 380, 410)),
            new("table", .9f, new PdfRectangle(20, 310, 380, 360)),
            new("picture", .9f, new PdfRectangle(20, 430, 160, 500)),
        ];
        var document = new ExtractionDocument("fixture");
        var regions = LayoutAssembler.AddPage(document, page, detections, default);
        Assert.Equal(4, regions.Count);
        Assert.Empty(regions[1].Words);
        Assert.Equal("LOCAL LAYOUT", string.Join(" ", regions[0].Words.Select(w => w.Text)));
        var elements = document.EnumerateContent().ToArray();
        Assert.IsType<ExtractionDocumentImageElement>(elements[0]);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(elements[1]).Role);
        var table = Assert.IsType<ExtractionDocumentTableElement>(elements[2]);
        Assert.Equal(0, table.Cells.RowCount);
        Assert.Contains("Native words", Assert.IsType<TextContent>(table.Content).Text);
        Assert.Contains("Unmatched text", Assert.IsType<TextContent>(elements[3].Content).Text);
        Assert.Same(regions[0], elements[1].Sources.Single().GetRawRepresentation());
        Assert.Equal(4, elements[1].Sources.Single().Polygon.Count);
        Assert.Equal("pageHeader", LayoutAssembler.Role("page_header"));
        Assert.Equal("pageFooter", LayoutAssembler.Role("page_footer"));
        Assert.Equal("heading", LayoutAssembler.Role("section_header"));
        Assert.Equal("list_item", LayoutAssembler.Role("list_item"));
        Assert.Null(LayoutAssembler.Role("text"));
    }

    [Fact]
    public void RasterLimitsCancellationAndOptionsFailExplicitly()
    {
        using var pdf = PdfDocument.Open(LayoutPdfFixture.Create());
        using var reader = DocLib.Instance.GetDocReader(LayoutPdfFixture.Create(), new PageDimensions(2d));
        using var page = reader.GetPageReader(0);
        Assert.Throws<InvalidDataException>(() => PageRaster.Render(page, pdf.GetPage(1), 10, default));
        Assert.Throws<OperationCanceledException>(() => PageRaster.Render(page, pdf.GetPage(1), 32_000_000, new(true)));
        Assert.Throws<OperationCanceledException>(() => new PageRaster(new byte[4], 1, 1, 1, 1).ToTensor(new(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxLayoutOptions { ConfidenceThreshold = float.NaN }.Snapshot());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxLayoutOptions { RenderDpi = 0 }.Snapshot());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxLayoutOptions { MaximumPagePixels = 0 }.Snapshot());
        Assert.Throws<OnnxRuntimeException>(() => new PdfPigOnnxDocumentExtractionClient("missing-model.onnx"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void ConservesVisibleWordsAcrossRegionsAndUnmatchedBlocks(int rotation)
    {
        using var pdf = PdfDocument.Open(LayoutPdfFixture.Create(rotation, crop: true));
        var page = pdf.GetPage(1);
        var document = new ExtractionDocument("conservation");
        var words = LayoutAssembler.AddPage(document, page,
            [new("title", .9f, new PdfRectangle(0, page.Height / 2, page.Width, page.Height))], default)
            .SelectMany(region => region.Words).ToList();
        foreach (var source in document.EnumerateContent().SelectMany(element => element.Sources))
        {
            if (source.GetRawRepresentation() is TextBlock block)
            {
                words.AddRange(block.TextLines.SelectMany(line => line.Words));
            }
        }

        string[] expected = "LOCAL LAYOUT Native words remain searchable. Layout detection uses the real page. Unmatched text is retained."
            .Split(' ').Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, words.Select(word => word.Text).Order(StringComparer.Ordinal));
        Assert.Equal(words.Count, words.Distinct().Count());
    }

    [Theory]
    [InlineData(.49, false)]
    [InlineData(.5, true)]
    [InlineData(1, true)]
    public void WordAssociationHonorsHalfAreaBoundary(double fraction, bool expected)
    {
        using var pdf = PdfDocument.Open(LayoutPdfFixture.Create());
        var page = pdf.GetPage(1);
        var word = page.GetWords(NearestNeighbourWordExtractor.Instance).Single(word => word.Text == "LOCAL");
        var box = LayoutAssembler.AxisAligned(word.BoundingBox);
        var region = new PdfRectangle(box.Left, box.Bottom, box.Left + box.Width * fraction, box.Top);
        var document = new ExtractionDocument("boundary");
        var regions = LayoutAssembler.AddPage(document, page, [new("text", .9f, region)], default);
        Assert.Equal(expected, regions[0].Words.Any(item => item.Text == "LOCAL"));
        string[] allText = document.EnumerateContent().Select(element => (element.Content as TextContent)?.Text ?? "").ToArray();
        Assert.Contains(allText, value => value.Contains("LOCAL", StringComparison.Ordinal));
    }

    [Fact]
    public void CompositesAlphaAndClipsPixelCoordinates()
    {
        var raster = new PageRaster([0, 0, 255, 128], 1, 1, 400, 550);
        var tensor = raster.ToTensor(default);
        Assert.Equal(255, tensor[0, 0, 0, 0]);
        Assert.Equal(127, tensor[0, 1, 0, 0]);
        Assert.Equal(127, tensor[0, 2, 0, 0]);
        Assert.Equal(127, tensor[0, 1, 639, 639]);
        var clipped = raster.ToPageBounds(-10, -20, 10, 20);
        Assert.Equal(new PdfRectangle(0, 0, 400, 550), clipped);
        var fractional = new PageRaster([], 533, 733, 400, 550).ToPageBounds(133.25f, 183.25f, 399.75f, 549.75f);
        Assert.Equal(new PdfRectangle(100, 137.5, 300, 412.5), fractional);
    }

    [Fact]
    public void CompositesRealPdfTransparencyOntoWhite()
    {
        byte[] bytes = LayoutPdfFixture.Create(transparency: true);
        using var pdf = PdfDocument.Open(bytes);
        using var reader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(2d));
        using var page = reader.GetPageReader(0);
        var raster = PageRaster.Render(page, pdf.GetPage(1), 32_000_000, default);
        int index = (1050 * raster.Width + 730) * 4;
        Assert.Equal(255, raster.Bgra[index + 1]);
        Assert.InRange(raster.Bgra[index + 3], (byte)126, (byte)129);
        var tensor = raster.ToTensor(default);
        int x = 365 * 640 / 400, y = 525 * 640 / 550;
        Assert.Equal(255, tensor[0, 1, y, x]);
        Assert.InRange(tensor[0, 0, y, x], (byte)126, (byte)129);
        Assert.InRange(tensor[0, 2, y, x], (byte)126, (byte)129);
    }

    [Fact]
    public void RejectsWrongGraphWithoutTakingBorrowedSessionOwnership()
    {
        // Generated ONNX opset-18 Identity(float[1]) graph, not model weights.
        byte[] bytes = Convert.FromBase64String("CAg6VAoZCgVpbnB1dBIGb3V0cHV0IghJZGVudGl0eRIMaW5jb21wYXRpYmxlWhMKBWlucHV0EgoKCAgBEgQKAggBYhQKBm91dHB1dBIKCggIARIECgIIAUIECgAQEg==");
        using var session = new InferenceSession(bytes);
        Assert.Throws<ArgumentException>(() => new PdfPigOnnxDocumentExtractionClient(session));
        Assert.Single(session.InputMetadata);
        var input = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(new[] { 1 });
        input[0] = 42;
        using var results = session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        Assert.Equal(42, results.Single().AsTensor<float>()[0]);
    }

    [Fact]
    public async Task RealHeronModelToDocumentAndBorrowedOwnership()
    {
        string path = Environment.GetEnvironmentVariable("DOCUMENT_EXTRACTION_HERON_MODEL")
            ?? throw new InvalidOperationException("Set DOCUMENT_EXTRACTION_HERON_MODEL to the explicitly acquired Heron model.");

        Assert.Equal("59C81A3A2923042D85034FFC487F8F47E4854117E879AEF89B2B9F728FB4922A",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))));
        using var session = new InferenceSession(path);
        var options = new OnnxLayoutOptions { ConfidenceThreshold = .25f };
        var client = new PdfPigOnnxDocumentExtractionClient(session, options);
        options.ConfidenceThreshold = 1;
        using var input = new MemoryStream(LayoutPdfFixture.Create(pages: 2));
        var document = await client.ExtractAsync(input, "real-heron", "application/pdf");
        Assert.True(input.CanRead);
        Assert.Equal(2, document.SourcePages.Count);
        var regions = Assert.IsAssignableFrom<IReadOnlyList<PdfPigLayoutRegion>>(document.RawRepresentation);
        Assert.NotEmpty(regions);
        Assert.Contains(regions, region => region.Words.Count > 0);
        Assert.All(regions, region => Assert.InRange(region.Confidence, .25f, 1));
        string text = string.Join(" ", document.EnumerateContent().Select(element => (element.Content as TextContent)?.Text));
        Assert.Contains("LOCAL LAYOUT", text);
        Assert.Contains("Unmatched text", text);
        Assert.Same(session, client.GetService(typeof(InferenceSession)));
        Assert.Null(client.GetService(typeof(InferenceSession), "key"));
        Assert.Same(client, client.GetService(typeof(DocumentExtractionClient)));
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(input, "x", "image/png"));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(input, "x", "application/pdf", new() { ExtractorName = "other" }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExtractAsync(input, "x", "application/pdf", cancellationToken: new(true)));
        client.Dispose();
        client.Dispose();
        HeronModel.Validate(session);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ExtractAsync(input, "x", "application/pdf"));
        using var owned = new PdfPigOnnxDocumentExtractionClient(path);
        using var ownedInput = new MemoryStream(LayoutPdfFixture.Create(rotation: 90, crop: true));
        var rotated = await owned.ExtractAsync(ownedInput, "rotated", "application/pdf");
        Assert.Single(rotated.SourcePages);
        Assert.NotEmpty(Assert.IsAssignableFrom<IReadOnlyList<PdfPigLayoutRegion>>(rotated.RawRepresentation));
        string rotatedText = string.Join(" ", rotated.EnumerateContent().Select(element =>
            element is ExtractionDocumentImageElement image ? image.Text : (element.Content as TextContent)?.Text));
        rotatedText = string.Join(" ", rotatedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("LOCAL LAYOUT", rotatedText);
        Assert.Contains("Unmatched text", rotatedText);
        Assert.DoesNotContain("OUTSIDE_CROP", rotatedText);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var cancellationRaster = new PageRaster(new byte[400 * 550 * 4], 400, 550, 400, 550);
        Assert.ThrowsAny<OperationCanceledException>(() => HeronModel.Detect(session, cancellationRaster, .5f, cancel.Token));
        Assert.NotEmpty(HeronModel.Detect(session, cancellationRaster, 0, default));
        owned.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owned.GetService(typeof(InferenceSession)));
        using var afterDispose = new MemoryStream(LayoutPdfFixture.Create());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owned.ExtractAsync(afterDispose, "disposed", "application/pdf"));
        using var unsupportedUnit = new MemoryStream(LayoutPdfFixture.Create(userUnit: true));
        input.Position = 0;
        using var supported = new PdfPigOnnxDocumentExtractionClient(session);
        await Assert.ThrowsAsync<NotSupportedException>(() => supported.ExtractAsync(unsupportedUnit, "unit", "application/pdf"));
        output.WriteLine($"REAL Heron model: {regions.Count} detections, 2 pages, content={text}; rotated/cropped page accepted.");
    }
}
