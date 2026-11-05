using CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;
using SkiaSharp;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.Vision.UnitTests;

public sealed class PdfPageRasterizerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 600, 800)]
    [InlineData(2, 1000, 1200)]
    [InlineData(3, 1400, 1600)]
    public void RenderPage_LocalPdf_ReturnsDecodablePngForRequestedOneBasedPage(int number, int width, int height)
    {
        using var pdf = PdfPageRenderer.Open(PdfFixture.Create());
        byte[] png = PdfPageRasterizer.RenderPage(pdf, number);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        if (number == 3)
        {
            SKColor pixel = bitmap.GetPixel(60, height - 60);
            Assert.Equal((byte)255, pixel.Red);
            Assert.Equal((byte)0, pixel.Green);
            Assert.Equal((byte)0, pixel.Blue);
        }
        else if (number == 2)
        {
            Assert.Equal(SKColors.White, bitmap.GetPixel(60, 60));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void RenderPage_InvalidPageNumber_Rejects(int number)
    {
        using var pdf = PdfPageRenderer.Open(PdfFixture.Create());
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => PdfPageRasterizer.RenderPage(pdf, number));
        output.WriteLine($"{error.GetType().FullName}: {error.Message}");
    }

    [Fact]
    public void RenderPage_InvalidPdf_Rejects()
    {
        var error = Assert.Throws<PdfDocumentFormatException>(() => PdfPageRenderer.Open([1, 2, 3]));
        output.WriteLine($"{error.GetType().FullName}: {error.Message}");
    }

    [Fact]
    public void RenderPage_RepeatedCalls_ReturnManagedBytesAfterNativeCleanup()
    {
        byte[] first;
        byte[] second;
        using (var pdf = PdfPageRenderer.Open(PdfFixture.Create()))
        {
            first = PdfPageRasterizer.RenderPage(pdf, 1);
            second = PdfPageRasterizer.RenderPage(pdf, 3);
        }
        using var firstBitmap = SKBitmap.Decode(first);
        using var secondBitmap = SKBitmap.Decode(second);
        Assert.Equal(600, firstBitmap.Width);
        Assert.Equal(1600, secondBitmap.Height);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void RenderPage_CanceledRenderDoesNotPoisonBorrowedDocument()
    {
        using var pdf = PdfPageRenderer.Open(PdfFixture.Create());
        Assert.Throws<OperationCanceledException>(() => PdfPageRasterizer.RenderPage(pdf, 1, new(true)));
        using var bitmap = SKBitmap.Decode(PdfPageRasterizer.RenderPage(pdf, 1));
        Assert.Equal(600, bitmap.Width);
    }
}
