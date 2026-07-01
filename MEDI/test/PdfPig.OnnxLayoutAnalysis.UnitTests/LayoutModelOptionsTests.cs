#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.Models;
using Xunit;

public class LayoutModelOptionsTests
{
    #region Default values

    [Fact]
    public void DefaultOptions_InputWidth()
    {
        var options = new LayoutModelOptions();
        Assert.Equal(640, options.InputWidth);
    }

    [Fact]
    public void DefaultOptions_InputHeight()
    {
        var options = new LayoutModelOptions();
        Assert.Equal(640, options.InputHeight);
    }

    [Fact]
    public void DefaultOptions_ResizeMode()
    {
        var options = new LayoutModelOptions();
        Assert.Equal(ResizeMode.Exact, options.Resize);
    }

    [Fact]
    public void DefaultOptions_PixelFormat()
    {
        var options = new LayoutModelOptions();
        Assert.Equal(PixelFormat.Uint8Chw, options.PixelFormat);
    }

    [Fact]
    public void DefaultOptions_NormalizationIsNull()
    {
        var options = new LayoutModelOptions();
        Assert.Null(options.Normalization);
    }

    [Fact]
    public void DefaultOptions_ConfidenceThreshold()
    {
        var options = new LayoutModelOptions();
        Assert.Equal(0.3f, options.ConfidenceThreshold, 4);
    }

    [Fact]
    public void DefaultOptions_NmsIouThreshold()
    {
        var options = new LayoutModelOptions();
        Assert.Equal(0.45f, options.NmsIouThreshold, 4);
    }

    [Fact]
    public void DefaultOptions_RequiresNmsFalse()
    {
        var options = new LayoutModelOptions();
        Assert.False(options.RequiresNms);
    }

    [Fact]
    public void DefaultOptions_OutputBboxFormat()
    {
        var options = new LayoutModelOptions();
        Assert.Equal(BboxFormat.CxCyWh, options.OutputBboxFormat);
    }

    [Fact]
    public void DefaultOptions_ClassLabelsNull()
    {
        var options = new LayoutModelOptions();
        Assert.Null(options.ClassLabels);
    }

    #endregion

    #region Custom init values

    [Fact]
    public void Options_CustomValues()
    {
        var labels = new Dictionary<int, string> { [0] = "text", [1] = "image" };
        var options = new LayoutModelOptions
        {
            InputWidth = 1024,
            InputHeight = 768,
            Resize = ResizeMode.Letterbox,
            PixelFormat = PixelFormat.Float32Chw,
            Normalization = WellKnownNormalizations.ImageNet,
            ConfidenceThreshold = 0.5f,
            NmsIouThreshold = 0.6f,
            RequiresNms = true,
            OutputBboxFormat = BboxFormat.Xyxy,
            ClassLabels = labels
        };

        Assert.Equal(1024, options.InputWidth);
        Assert.Equal(768, options.InputHeight);
        Assert.Equal(ResizeMode.Letterbox, options.Resize);
        Assert.Equal(PixelFormat.Float32Chw, options.PixelFormat);
        Assert.NotNull(options.Normalization);
        Assert.Equal(0.5f, options.ConfidenceThreshold, 4);
        Assert.Equal(0.6f, options.NmsIouThreshold, 4);
        Assert.True(options.RequiresNms);
        Assert.Equal(BboxFormat.Xyxy, options.OutputBboxFormat);
        Assert.Equal(2, options.ClassLabels!.Count);
    }

    #endregion

    #region WellKnownNormalizations

    [Fact]
    public void ImageNet_HasCorrectMean()
    {
        var norm = WellKnownNormalizations.ImageNet;

        Assert.Equal(3, norm.Mean.Length);
        Assert.Equal(0.485f, norm.Mean[0], 4);
        Assert.Equal(0.456f, norm.Mean[1], 4);
        Assert.Equal(0.406f, norm.Mean[2], 4);
    }

    [Fact]
    public void ImageNet_HasCorrectStd()
    {
        var norm = WellKnownNormalizations.ImageNet;

        Assert.Equal(3, norm.Std.Length);
        Assert.Equal(0.229f, norm.Std[0], 4);
        Assert.Equal(0.224f, norm.Std[1], 4);
        Assert.Equal(0.225f, norm.Std[2], 4);
    }

    [Fact]
    public void ZeroToOne_HasCorrectMean()
    {
        var norm = WellKnownNormalizations.ZeroToOne;

        Assert.Equal(3, norm.Mean.Length);
        Assert.Equal(0f, norm.Mean[0], 4);
        Assert.Equal(0f, norm.Mean[1], 4);
        Assert.Equal(0f, norm.Mean[2], 4);
    }

    [Fact]
    public void ZeroToOne_HasCorrectStd()
    {
        var norm = WellKnownNormalizations.ZeroToOne;

        Assert.Equal(3, norm.Std.Length);
        float expected = 1f / 255f;
        Assert.Equal(expected, norm.Std[0], 6);
        Assert.Equal(expected, norm.Std[1], 6);
        Assert.Equal(expected, norm.Std[2], 6);
    }

    #endregion

    #region Enums

    [Theory]
    [InlineData(ResizeMode.Exact)]
    [InlineData(ResizeMode.Letterbox)]
    [InlineData(ResizeMode.AspectPreserve)]
    public void ResizeMode_AllValuesExist(ResizeMode mode)
    {
        Assert.True(Enum.IsDefined(mode));
    }

    [Theory]
    [InlineData(PixelFormat.Uint8Chw)]
    [InlineData(PixelFormat.Float32Chw)]
    public void PixelFormat_AllValuesExist(PixelFormat format)
    {
        Assert.True(Enum.IsDefined(format));
    }

    [Theory]
    [InlineData(BboxFormat.CxCyWh)]
    [InlineData(BboxFormat.Xyxy)]
    [InlineData(BboxFormat.Xywh)]
    public void BboxFormat_AllValuesExist(BboxFormat format)
    {
        Assert.True(Enum.IsDefined(format));
    }

    [Fact]
    public void ResizeMode_HasThreeValues()
    {
        Assert.Equal(3, Enum.GetValues<ResizeMode>().Length);
    }

    [Fact]
    public void PixelFormat_HasTwoValues()
    {
        Assert.Equal(2, Enum.GetValues<PixelFormat>().Length);
    }

    [Fact]
    public void BboxFormat_HasThreeValues()
    {
        Assert.Equal(3, Enum.GetValues<BboxFormat>().Length);
    }

    #endregion

    #region ImageNormalization record

    [Fact]
    public void ImageNormalization_StoresMeanAndStd()
    {
        float[] mean = [0.1f, 0.2f, 0.3f];
        float[] std = [0.4f, 0.5f, 0.6f];

        var norm = new ImageNormalization(mean, std);

        Assert.Same(mean, norm.Mean);
        Assert.Same(std, norm.Std);
    }

    [Fact]
    public void ImageNormalization_RecordEquality()
    {
        float[] mean = [0.5f, 0.5f, 0.5f];
        float[] std = [1f, 1f, 1f];

        var a = new ImageNormalization(mean, std);
        var b = new ImageNormalization(mean, std);

        Assert.Equal(a, b);
    }

    #endregion
}
#endif
