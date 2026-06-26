#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using UglyToad.PdfPig.PdfFonts;
using Xunit;

public class AnnotatedTextBlockTests
{
    [Fact]
    public void Constructor_SetsLabelAndConfidence()
    {
        var word = CreateWord(new PdfRectangle(10, 10, 50, 25));
        var line = new TextLine(new[] { word });
        var block = new AnnotatedTextBlock(new[] { line }, "table", 0.95f);

        Assert.Equal("table", block.Label);
        Assert.Equal(0.95f, block.Confidence, 4);
    }

    [Fact]
    public void TextProperty_ReturnsLineText()
    {
        var word = CreateWord(new PdfRectangle(10, 10, 50, 25));
        var line = new TextLine(new[] { word });
        var block = new AnnotatedTextBlock(new[] { line }, "text", 0.9f);

        Assert.Equal("a", block.Text);
    }

    [Fact]
    public void DefaultSeparator_IsNewline()
    {
        var w1 = CreateWord(new PdfRectangle(10, 100, 50, 120));
        var w2 = CreateWord(new PdfRectangle(10, 50, 50, 70));
        var line1 = new TextLine(new[] { w1 });
        var line2 = new TextLine(new[] { w2 });
        var block = new AnnotatedTextBlock(new[] { line1, line2 }, "text", 0.8f);

        Assert.Equal("a\na", block.Text);
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
