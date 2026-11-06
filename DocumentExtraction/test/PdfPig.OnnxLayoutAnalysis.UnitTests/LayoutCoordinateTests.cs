// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class LayoutCoordinateTests
{
    [Theory]
    [InlineData(1224, 1584)]
    [InlineData(1584, 1224)]
    [InlineData(533, 733)]
    public void TargetSizesUseOriginalNonSquareRasterDimensions(int width, int height)
    {
        var raster = new PageRaster([], width, height, 612, 792);
        var corrected = HeronModel.CreateTargetSizes(raster, true);
        var legacy = HeronModel.CreateTargetSizes(raster, false);

        Assert.Equal(new[] { 1, 2 }, corrected.Dimensions.ToArray());
        Assert.Equal(new[] { 1, 2 }, legacy.Dimensions.ToArray());
        Assert.Equal(new long[] { width, height }, corrected.ToArray());
        Assert.Equal(new long[] { height, width }, legacy.ToArray());
        Assert.NotEqual(corrected.ToArray(), legacy.ToArray());
        Assert.Empty(raster.Bgra);
    }

    [Fact]
    public void CoordinateOptionDefaultsToLegacyAndIsCapturedIndependently()
    {
        var defaults = new OnnxLayoutOptions().Snapshot();
        Assert.False(defaults.UseCorrectedLayoutCoordinates);
        var options = new OnnxLayoutOptions { UseCorrectedLayoutCoordinates = true };
        var snapshot = options.Snapshot();
        options.UseCorrectedLayoutCoordinates = false;

        Assert.True(snapshot.UseCorrectedLayoutCoordinates);
        Assert.False(snapshot.UseColumnReadingOrder);
        Assert.Null(snapshot.TableStructure);
        Assert.Null(snapshot.Ocr);
        Assert.Equal(defaults.ConfidenceThreshold, snapshot.ConfidenceThreshold);
        Assert.Equal(defaults.RenderDpi, snapshot.RenderDpi);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(180, false)]
    [InlineData(270, false)]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(180, true)]
    [InlineData(270, true)]
    public void CorrectedSizesPreserveRenderedCropRotationFrame(int rotation, bool crop)
    {
        using var pdf = PdfPageRenderer.Open(LayoutPdfFixture.Create(rotation, crop));
        var page = pdf.GetPage(1);
        var raster = PageRaster.Render(pdf, page, 2, 32_000_000, default);
        var sizes = HeronModel.CreateTargetSizes(raster, true);

        Assert.NotEqual(raster.Width, raster.Height);
        Assert.Equal(raster.Width, sizes[0, 0]);
        Assert.Equal(raster.Height, sizes[0, 1]);
        var fullPage = raster.ToPageBounds(0, 0, sizes[0, 0], sizes[0, 1]);
        AssertBounds(new(0, 0, page.Width, page.Height), fullPage);
        var region = raster.ToPageBounds(
            sizes[0, 0] * .1f, sizes[0, 1] * .2f,
            sizes[0, 0] * .6f, sizes[0, 1] * .7f);
        AssertBounds(new(page.Width * .1, page.Height * .3,
            page.Width * .6, page.Height * .8), region);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BothConstructionPathsCaptureAndWireCoordinateOption(bool borrowed, bool corrected)
    {
        byte[] graph = CreateGraph();
        string path = Path.Combine(Path.GetTempPath(), $"layout-coordinate-{Guid.NewGuid():N}.onnx");
        await File.WriteAllBytesAsync(path, graph);
        try
        {
            using var session = new InferenceSession(graph);
            var options = new OnnxLayoutOptions { UseCorrectedLayoutCoordinates = corrected, LayoutModelPath = borrowed ? null : path };
            using var client = borrowed
                ? new PdfPigOnnxDocumentExtractionClient(session, options)
                : new PdfPigOnnxDocumentExtractionClient(options);
            options.UseCorrectedLayoutCoordinates = !corrected;
            using var input = new MemoryStream(LayoutPdfFixture.Create());
            var document = await client.ExtractAsync(input, "contract", "application/pdf");
            var regions = Assert.IsAssignableFrom<IReadOnlyList<PdfPigLayoutRegion>>(document.RawRepresentation);

            Assert.Equal(300, regions.Count);
            PdfRectangle expected = corrected ? new(40, 165, 240, 440) : new(55, 270, 330, 470);
            Assert.All(regions, region =>
            {
                Assert.Equal("text", region.Label);
                AssertBounds(expected, region.Bounds);
            });
            Assert.True(input.CanRead);
            client.Dispose();
            Assert.Equal(2, session.InputMetadata.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Synthetic XYXY scaling graph, not Heron weights or a quality fixture.
    internal static byte[] CreateGraph() => Convert.FromBase64String(
        "CAg6yQUKPQoRb3JpZ190YXJnZXRfc2l6ZXMKB2luZGljZXMSCnh5eHlfc2l6ZXMiBkdhdGhlcioLCgRheGlzGAGgAQIKKgoKeHl4eV9zaXplcxILZmxvYXRfc2l6ZXMiBENhc3QqCQoCdG8YAaABAgovCgtmbG9hdF9zaXplcwoEYXhpcxIPYnJvYWRjYXN0X3NpemVzIglVbnNxdWVlemUKNQoPYnJvYWRjYXN0X3NpemVzCg9ub3JtYWxpemVkX3h5eHkSDHNjYWxlZF9ib3hlcyIDTXVsCiQKDHNjYWxlZF9ib3hlcwoFc2hhcGUSBWJveGVzIgZFeHBhbmQKQwoMb3V0cHV0X3NoYXBlEgZsYWJlbHMiD0NvbnN0YW50T2ZTaGFwZSoaCgV2YWx1ZSoOCAEQBzoBCUIFbGFiZWygAQQKRgoMb3V0cHV0X3NoYXBlEgZzY29yZXMiD0NvbnN0YW50T2ZTaGFwZSodCgV2YWx1ZSoRCAEQASIEZmZmP0IFc2NvcmWgAQQSJ3N5bnRoZXRpYy13aC1jb250cmFjdC1ub3QtbW9kZWwtd2VpZ2h0cyoTCAQQBzoEAAEAAUIHaW5kaWNlcyoNCAEQBzoBAUIEYXhpcyoRCAMQBzoEAawCBEIFc2hhcGUqFwgCEAc6AwGsAkIMb3V0cHV0X3NoYXBlKisIAQgBCAQQASIQzczMPc3MTD6amRk/MzMzP0IPbm9ybWFsaXplZF94eXh5WiIKBmltYWdlcxIYChYIAhISCgIIAQoCCAMKAwiABQoDCIAFWiMKEW9yaWdfdGFyZ2V0X3NpemVzEg4KDAgHEggKAggBCgIIAmIZCgZsYWJlbHMSDwoNCAcSCQoCCAEKAwisAmIcCgVib3hlcxITChEIARINCgIIAQoDCKwCCgIIBGIZCgZzY29yZXMSDwoNCAESCQoCCAEKAwisAkIECgAQEg==");

    private static void AssertBounds(PdfRectangle expected, PdfRectangle actual)
    {
        Assert.InRange(Math.Abs(expected.Left - actual.Left), 0, .0001);
        Assert.InRange(Math.Abs(expected.Bottom - actual.Bottom), 0, .0001);
        Assert.InRange(Math.Abs(expected.Right - actual.Right), 0, .0001);
        Assert.InRange(Math.Abs(expected.Top - actual.Top), 0, .0001);
    }
}
