using CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;
using Docnet.Core.Exceptions;
using SkiaSharp;

namespace CommunityToolkit.DocumentExtraction.Vision.UnitTests;

public sealed class PdfPageRasterizerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 600, 800)]
    [InlineData(2, 1000, 1200)]
    [InlineData(3, 1400, 1600)]
    public void RenderPage_LocalPdf_ReturnsDecodablePngForRequestedOneBasedPage(int number, int width, int height)
    {
        byte[] png = PdfPageRasterizer.RenderPage(PdfFixture.Create(), number);
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
            Assert.Equal((byte)0, bitmap.GetPixel(60, 60).Alpha);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void RenderPage_InvalidPageNumber_Rejects(int number)
    {
        var error = Assert.Throws<DocnetException>(() => PdfPageRasterizer.RenderPage(PdfFixture.Create(), number));
        output.WriteLine($"{error.GetType().FullName}: {error.Message}");
        Assert.Equal($"failed to open page for page index {number - 1}", error.Message);
    }

    [Fact]
    public void RenderPage_InvalidPdf_Rejects()
    {
        var error = Assert.Throws<DocnetLoadDocumentException>(() => PdfPageRasterizer.RenderPage([1, 2, 3], 1));
        output.WriteLine($"{error.GetType().FullName}: {error.Message}");
        Assert.Contains("file not in PDF format or corrupted", error.Message);
    }

    [Fact]
    public void RenderPage_RepeatedCalls_ReturnManagedBytesAfterNativeCleanup()
    {
        byte[] first = PdfPageRasterizer.RenderPage(PdfFixture.Create(), 1);
        byte[] second = PdfPageRasterizer.RenderPage(PdfFixture.Create(), 3);
        using var firstBitmap = SKBitmap.Decode(first);
        using var secondBitmap = SKBitmap.Decode(second);
        Assert.Equal(600, firstBitmap.Width);
        Assert.Equal(1600, secondBitmap.Height);
        Assert.NotSame(first, second);
    }
}
