using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using System;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// Static utility methods for image preprocessing before ONNX model inference.
/// </summary>
public static class ImagePreprocessing
{
    /// <summary>
    /// Resize an image maintaining aspect ratio and pad to the target size (letterboxing).
    /// </summary>
    /// <param name="image">Source image.</param>
    /// <param name="targetW">Target width.</param>
    /// <param name="targetH">Target height.</param>
    /// <param name="padColor">Color to use for padding.</param>
    /// <param name="scale">The scale factor applied to the image.</param>
    /// <param name="padX">Horizontal padding offset in pixels.</param>
    /// <param name="padY">Vertical padding offset in pixels.</param>
    /// <returns>A new letterboxed bitmap.</returns>
    public static SKBitmap Letterbox(SKBitmap image, int targetW, int targetH, SKColor padColor, out float scale, out int padX, out int padY)
    {
        ArgumentNullException.ThrowIfNull(image);

        float scaleX = (float)targetW / image.Width;
        float scaleY = (float)targetH / image.Height;
        scale = Math.Min(scaleX, scaleY);

        int newW = (int)(image.Width * scale);
        int newH = (int)(image.Height * scale);
        padX = (targetW - newW) / 2;
        padY = (targetH - newH) / 2;

        var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
        var result = new SKBitmap(info);

        using var canvas = new SKCanvas(result);
        canvas.Clear(padColor);

        var destRect = SKRect.Create(padX, padY, newW, newH);
        using var skImage = SKImage.FromBitmap(image);
        canvas.DrawImage(skImage, destRect, new SKSamplingOptions(SKCubicResampler.Mitchell));

        return result;
    }

    /// <summary>
    /// Resize an image to exact dimensions, stretching if necessary.
    /// </summary>
    /// <param name="image">Source image.</param>
    /// <param name="targetW">Target width.</param>
    /// <param name="targetH">Target height.</param>
    /// <returns>A new resized bitmap.</returns>
    public static SKBitmap ResizeExact(SKBitmap image, int targetW, int targetH)
    {
        ArgumentNullException.ThrowIfNull(image);

        var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
        var result = new SKBitmap(info);

        using var canvas = new SKCanvas(result);
        var destRect = SKRect.Create(0, 0, targetW, targetH);
        using var skImage = SKImage.FromBitmap(image);
        canvas.DrawImage(skImage, destRect, new SKSamplingOptions(SKCubicResampler.Mitchell));

        return result;
    }

    /// <summary>
    /// Extract a CHW byte tensor [1, 3, H, W] from an image.
    /// Channel order is RGB.
    /// </summary>
    /// <param name="image">Source image (must be RGBA8888 or compatible).</param>
    /// <returns>A dense tensor with shape [1, 3, H, W].</returns>
    public static DenseTensor<byte> ToChwUint8(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);

        int w = image.Width;
        int h = image.Height;
        var tensor = new DenseTensor<byte>([1, 3, h, w]);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var pixel = image.GetPixel(x, y);
                tensor[0, 0, y, x] = pixel.Red;
                tensor[0, 1, y, x] = pixel.Green;
                tensor[0, 2, y, x] = pixel.Blue;
            }
        }

        return tensor;
    }

    /// <summary>
    /// Extract a CHW float tensor [1, 3, H, W] from an image.
    /// Values are normalized to [0, 1] range. Channel order is RGB.
    /// </summary>
    /// <param name="image">Source image (must be RGBA8888 or compatible).</param>
    /// <returns>A dense tensor with shape [1, 3, H, W].</returns>
    public static DenseTensor<float> ToChwFloat(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);

        int w = image.Width;
        int h = image.Height;
        var tensor = new DenseTensor<float>([1, 3, h, w]);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var pixel = image.GetPixel(x, y);
                tensor[0, 0, y, x] = pixel.Red / 255f;
                tensor[0, 1, y, x] = pixel.Green / 255f;
                tensor[0, 2, y, x] = pixel.Blue / 255f;
            }
        }

        return tensor;
    }

    /// <summary>
    /// Apply ImageNet normalization in-place: mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225].
    /// </summary>
    /// <param name="chw">The CHW float buffer (length must be 3 * width * height).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    public static void NormalizeImageNet(Span<float> chw, int width, int height)
    {
        ReadOnlySpan<float> mean = [0.485f, 0.456f, 0.406f];
        ReadOnlySpan<float> std = [0.229f, 0.224f, 0.225f];
        Normalize(chw, width, height, mean, std);
    }

    /// <summary>
    /// Apply custom per-channel normalization in-place: (value - mean) / std.
    /// </summary>
    /// <param name="chw">The CHW float buffer (length must be 3 * width * height).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="mean">Per-channel mean values (length 3).</param>
    /// <param name="std">Per-channel standard deviation values (length 3).</param>
    public static void Normalize(Span<float> chw, int width, int height, ReadOnlySpan<float> mean, ReadOnlySpan<float> std)
    {
        if (mean.Length != 3)
        {
            throw new ArgumentException("Mean must have exactly 3 elements.", nameof(mean));
        }

        if (std.Length != 3)
        {
            throw new ArgumentException("Std must have exactly 3 elements.", nameof(std));
        }

        int channelSize = width * height;

        for (int c = 0; c < 3; c++)
        {
            int offset = c * channelSize;
            float m = mean[c];
            float s = std[c];

            for (int i = 0; i < channelSize; i++)
            {
                chw[offset + i] = (chw[offset + i] - m) / s;
            }
        }
    }
}
