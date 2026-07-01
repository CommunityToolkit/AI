#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using System.Collections.Generic;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using UglyToad.PdfPig.PdfFonts;
using Xunit;

public class PageImageRendererTests
{
    [Fact]
    public void RenderWords_NullWords_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PageImageRenderer.RenderWords(null!, 595, 842));
    }

    [Fact]
    public void RenderWords_DefaultDpi_CorrectBitmapDimensions()
    {
        using var bitmap = PageImageRenderer.RenderWords(
            new List<Word>(), 595, 842);

        Assert.Equal(1239, bitmap.Width);
        Assert.Equal(1754, bitmap.Height);
    }

    [Fact]
    public void RenderWords_CustomDpi_ScalesDimensions()
    {
        using var bitmap = PageImageRenderer.RenderWords(
            new List<Word>(), 595, 842, dpi: 72);

        Assert.Equal(595, bitmap.Width);
        Assert.Equal(842, bitmap.Height);
    }

    [Fact]
    public void RenderWords_EmptyWordsList_ReturnsWhiteBitmap()
    {
        using var bitmap = PageImageRenderer.RenderWords(
            new List<Word>(), 595, 842);

        Assert.NotNull(bitmap);
        Assert.Equal(1239, bitmap.Width);
        Assert.Equal(1754, bitmap.Height);
        Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void RenderWords_WithWords_RendersNonWhitePixels()
    {
        var words = new List<Word>
        {
            CreateWord(new PdfRectangle(10, 10, 100, 30))
        };

        using var bitmap = PageImageRenderer.RenderWords(words, 200, 200, dpi: 72);

        // The word occupies a region — at least one pixel should be non-white (black fill)
        bool hasNonWhite = false;
        for (int y = 0; y < bitmap.Height && !hasNonWhite; y++)
        {
            for (int x = 0; x < bitmap.Width && !hasNonWhite; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    hasNonWhite = true;
                }
            }
        }

        Assert.True(hasNonWhite, "Expected at least one non-white pixel from rendered word bounding box.");
    }

    private static Word CreateWord(PdfRectangle boundingBox)
    {
        var letter = new Letter(
            "a",
            boundingBox,
            boundingBox,
            boundingBox.BottomLeft,
            boundingBox.BottomRight,
            10, 1,
            (FontDetails)null!,
            TextRenderingMode.NeitherClip,
            null!, null!,
            0, 0);
        return new Word(new[] { letter });
    }
}
#endif
