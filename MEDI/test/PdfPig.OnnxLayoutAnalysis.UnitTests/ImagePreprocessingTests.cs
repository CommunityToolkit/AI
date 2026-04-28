#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using SkiaSharp;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using Xunit;

public class ImagePreprocessingTests
{
    #region ResizeExact

    [Fact]
    public void ResizeExact_ProducesCorrectDimensions()
    {
        using var src = CreateSolidBitmap(100, 200, SKColors.Red);
        using var result = ImagePreprocessing.ResizeExact(src, 50, 75);

        Assert.Equal(50, result.Width);
        Assert.Equal(75, result.Height);
    }

    [Fact]
    public void ResizeExact_SquareImage()
    {
        using var src = CreateSolidBitmap(64, 64, SKColors.Blue);
        using var result = ImagePreprocessing.ResizeExact(src, 640, 640);

        Assert.Equal(640, result.Width);
        Assert.Equal(640, result.Height);
    }

    [Fact]
    public void ResizeExact_1x1Image()
    {
        using var src = CreateSolidBitmap(1, 1, SKColors.Green);
        using var result = ImagePreprocessing.ResizeExact(src, 10, 10);

        Assert.Equal(10, result.Width);
        Assert.Equal(10, result.Height);
    }

    [Fact]
    public void ResizeExact_Upscale()
    {
        using var src = CreateSolidBitmap(10, 10, SKColors.White);
        using var result = ImagePreprocessing.ResizeExact(src, 640, 480);

        Assert.Equal(640, result.Width);
        Assert.Equal(480, result.Height);
    }

    [Fact]
    public void ResizeExact_NullImage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ImagePreprocessing.ResizeExact(null!, 10, 10));
    }

    #endregion

    #region Letterbox

    [Fact]
    public void Letterbox_ProducesTargetDimensions()
    {
        using var src = CreateSolidBitmap(100, 200, SKColors.Red);
        using var result = ImagePreprocessing.Letterbox(src, 640, 640, SKColors.Gray, out _, out _, out _);

        Assert.Equal(640, result.Width);
        Assert.Equal(640, result.Height);
    }

    [Fact]
    public void Letterbox_PreservesAspectRatio_TallImage()
    {
        // Tall image (100×200): scale limited by height → 640/200=3.2
        using var src = CreateSolidBitmap(100, 200, SKColors.Red);
        using var result = ImagePreprocessing.Letterbox(src, 640, 640, SKColors.Gray, out float scale, out int padX, out int padY);

        Assert.Equal(640 / 200f, scale, 3);
        Assert.True(padX > 0, "Tall image should have horizontal padding");
        Assert.Equal(0, padY);
    }

    [Fact]
    public void Letterbox_PreservesAspectRatio_WideImage()
    {
        // Wide image (200×100): scale limited by width → 640/200=3.2
        using var src = CreateSolidBitmap(200, 100, SKColors.Blue);
        using var result = ImagePreprocessing.Letterbox(src, 640, 640, SKColors.Gray, out float scale, out int padX, out int padY);

        Assert.Equal(640 / 200f, scale, 3);
        Assert.Equal(0, padX);
        Assert.True(padY > 0, "Wide image should have vertical padding");
    }

    [Fact]
    public void Letterbox_SquareImage_NoPadding()
    {
        using var src = CreateSolidBitmap(100, 100, SKColors.Green);
        using var result = ImagePreprocessing.Letterbox(src, 640, 640, SKColors.Gray, out float scale, out int padX, out int padY);

        Assert.Equal(640 / 100f, scale, 3);
        Assert.Equal(0, padX);
        Assert.Equal(0, padY);
    }

    [Fact]
    public void Letterbox_PaddingColor_Applied()
    {
        using var src = CreateSolidBitmap(100, 200, SKColors.Red);
        using var result = ImagePreprocessing.Letterbox(src, 640, 640, SKColors.Gray, out _, out int padX, out _);

        // The top-left corner should be in the pad area (if padX > 0)
        if (padX > 0)
        {
            var pixel = result.GetPixel(0, 0);
            // Pad color should be gray
            Assert.Equal(SKColors.Gray.Red, pixel.Red);
            Assert.Equal(SKColors.Gray.Green, pixel.Green);
            Assert.Equal(SKColors.Gray.Blue, pixel.Blue);
        }
    }

    [Fact]
    public void Letterbox_NullImage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ImagePreprocessing.Letterbox(null!, 640, 640, SKColors.Gray, out _, out _, out _));
    }

    #endregion

    #region ToChwUint8

    [Fact]
    public void ToChwUint8_CorrectShape()
    {
        using var src = CreateSolidBitmap(4, 3, SKColors.Red);
        var tensor = ImagePreprocessing.ToChwUint8(src);

        Assert.Equal(4, tensor.Dimensions.Length); // [1, 3, H, W]
        Assert.Equal(1, tensor.Dimensions[0]);
        Assert.Equal(3, tensor.Dimensions[1]);
        Assert.Equal(3, tensor.Dimensions[2]); // height
        Assert.Equal(4, tensor.Dimensions[3]); // width
    }

    [Fact]
    public void ToChwUint8_RedImage_CorrectChannelValues()
    {
        using var src = CreateSolidBitmap(2, 2, SKColors.Red);
        var tensor = ImagePreprocessing.ToChwUint8(src);

        // Red channel = 255, Green = 0, Blue = 0
        Assert.Equal(255, tensor[0, 0, 0, 0]); // R
        Assert.Equal(0, tensor[0, 1, 0, 0]);   // G
        Assert.Equal(0, tensor[0, 2, 0, 0]);   // B
    }

    [Fact]
    public void ToChwUint8_WhiteImage_AllChannels255()
    {
        using var src = CreateSolidBitmap(2, 2, SKColors.White);
        var tensor = ImagePreprocessing.ToChwUint8(src);

        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    Assert.Equal(255, tensor[0, c, y, x]);
                }
            }
        }
    }

    [Fact]
    public void ToChwUint8_1x1Image()
    {
        using var src = CreateSolidBitmap(1, 1, new SKColor(10, 20, 30));
        var tensor = ImagePreprocessing.ToChwUint8(src);

        Assert.Equal(1, tensor.Dimensions[2]);
        Assert.Equal(1, tensor.Dimensions[3]);
        Assert.Equal(10, tensor[0, 0, 0, 0]);
        Assert.Equal(20, tensor[0, 1, 0, 0]);
        Assert.Equal(30, tensor[0, 2, 0, 0]);
    }

    [Fact]
    public void ToChwUint8_NullImage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ImagePreprocessing.ToChwUint8(null!));
    }

    #endregion

    #region ToChwFloat

    [Fact]
    public void ToChwFloat_CorrectShape()
    {
        using var src = CreateSolidBitmap(5, 7, SKColors.Blue);
        var tensor = ImagePreprocessing.ToChwFloat(src);

        Assert.Equal(4, tensor.Dimensions.Length);
        Assert.Equal(1, tensor.Dimensions[0]);
        Assert.Equal(3, tensor.Dimensions[1]);
        Assert.Equal(7, tensor.Dimensions[2]); // height
        Assert.Equal(5, tensor.Dimensions[3]); // width
    }

    [Fact]
    public void ToChwFloat_ValuesInZeroOneRange()
    {
        using var src = CreateSolidBitmap(3, 3, new SKColor(128, 64, 255));
        var tensor = ImagePreprocessing.ToChwFloat(src);

        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    float val = tensor[0, c, y, x];
                    Assert.InRange(val, 0f, 1f);
                }
            }
        }
    }

    [Fact]
    public void ToChwFloat_WhiteImage_AllOnes()
    {
        using var src = CreateSolidBitmap(2, 2, SKColors.White);
        var tensor = ImagePreprocessing.ToChwFloat(src);

        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(1f, tensor[0, c, 0, 0], 4);
        }
    }

    [Fact]
    public void ToChwFloat_BlackImage_AllZeros()
    {
        using var src = CreateSolidBitmap(2, 2, SKColors.Black);
        var tensor = ImagePreprocessing.ToChwFloat(src);

        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(0f, tensor[0, c, 0, 0], 4);
        }
    }

    [Fact]
    public void ToChwFloat_SpecificColor_CorrectNormalization()
    {
        using var src = CreateSolidBitmap(1, 1, new SKColor(128, 0, 255));
        var tensor = ImagePreprocessing.ToChwFloat(src);

        Assert.Equal(128f / 255f, tensor[0, 0, 0, 0], 3); // R
        Assert.Equal(0f, tensor[0, 1, 0, 0], 3);           // G
        Assert.Equal(1f, tensor[0, 2, 0, 0], 3);           // B
    }

    [Fact]
    public void ToChwFloat_NullImage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ImagePreprocessing.ToChwFloat(null!));
    }

    #endregion

    #region NormalizeImageNet

    [Fact]
    public void NormalizeImageNet_AppliesCorrectTransformation()
    {
        int w = 2, h = 2;
        int channelSize = w * h;
        var chw = new float[3 * channelSize];

        // Fill with 0.5 for all channels
        Array.Fill(chw, 0.5f);

        ImagePreprocessing.NormalizeImageNet(chw, w, h);

        // Expected: (0.5 - mean) / std per channel
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[] std = [0.229f, 0.224f, 0.225f];

        for (int c = 0; c < 3; c++)
        {
            float expected = (0.5f - mean[c]) / std[c];
            for (int i = 0; i < channelSize; i++)
            {
                Assert.Equal(expected, chw[c * channelSize + i], 4);
            }
        }
    }

    [Fact]
    public void NormalizeImageNet_ZeroInput_NegativeMeanDividedByStd()
    {
        int w = 1, h = 1;
        var chw = new float[3];

        ImagePreprocessing.NormalizeImageNet(chw, w, h);

        Assert.Equal(-0.485f / 0.229f, chw[0], 4);
        Assert.Equal(-0.456f / 0.224f, chw[1], 4);
        Assert.Equal(-0.406f / 0.225f, chw[2], 4);
    }

    #endregion

    #region Normalize (custom)

    [Fact]
    public void Normalize_CustomMeanStd()
    {
        int w = 2, h = 1;
        var chw = new float[6]; // 3 channels × 2 pixels
        Array.Fill(chw, 1.0f);

        float[] mean = [0.5f, 0.5f, 0.5f];
        float[] std = [0.5f, 0.5f, 0.5f];
        ImagePreprocessing.Normalize(chw, w, h, mean, std);

        // (1.0 - 0.5) / 0.5 = 1.0
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(1.0f, chw[i], 4);
        }
    }

    [Fact]
    public void Normalize_IdentityTransform()
    {
        int w = 1, h = 1;
        var chw = new float[] { 0.3f, 0.6f, 0.9f };

        float[] mean = [0f, 0f, 0f];
        float[] std = [1f, 1f, 1f];
        ImagePreprocessing.Normalize(chw, w, h, mean, std);

        // (x - 0) / 1 = x, unchanged
        Assert.Equal(0.3f, chw[0], 4);
        Assert.Equal(0.6f, chw[1], 4);
        Assert.Equal(0.9f, chw[2], 4);
    }

    [Fact]
    public void Normalize_InvalidMeanLength_Throws()
    {
        var chw = new float[3];
        float[] badMean = [0.5f, 0.5f]; // length 2 instead of 3
        float[] std = [1f, 1f, 1f];

        Assert.Throws<ArgumentException>(() =>
            ImagePreprocessing.Normalize(chw, 1, 1, badMean, std));
    }

    [Fact]
    public void Normalize_InvalidStdLength_Throws()
    {
        var chw = new float[3];
        float[] mean = [0.5f, 0.5f, 0.5f];
        float[] badStd = [1f]; // length 1 instead of 3

        Assert.Throws<ArgumentException>(() =>
            ImagePreprocessing.Normalize(chw, 1, 1, mean, badStd));
    }

    #endregion

    #region Helpers

    private static SKBitmap CreateSolidBitmap(int width, int height, SKColor color)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }

    #endregion
}
#endif
