using System;
using System.Collections.Generic;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>
/// Renders PDF pages to PNG images using SkiaSharp for use with vision LLMs.
/// Unlike <c>DocumentLayoutAnalysis.Onnx.PageImageRenderer</c> which renders word bounding
/// boxes as black rectangles for ML model input, this renderer draws actual text characters
/// to produce human/LLM-readable page images.
/// </summary>
public static class PageImageRenderer
{
    private const float DefaultFontScale = 12f;
    private const float MinFontSize = 2f;
    private const float PdfDpi = 72f;
    private const int PngQuality = 90;

    /// <summary>
    /// Renders an entire PDF page to a PNG byte array.
    /// </summary>
    /// <param name="page">The PDF page to render.</param>
    /// <param name="dpi">Target resolution in dots per inch. Defaults to 150.</param>
    /// <returns>A PNG-encoded byte array of the rendered page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    public static byte[] RenderPage(Page page, int dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(page);

        float scale = dpi / PdfDpi;
        int pixelWidth = Math.Max(1, (int)(page.Width * scale));
        int pixelHeight = Math.Max(1, (int)(page.Height * scale));

        using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var fontCache = new Dictionary<float, SKFont>();
        try
        {
            foreach (var letter in page.Letters)
            {
                float x = (float)letter.Location.X * scale;
                float y = (float)(page.Height - letter.Location.Y) * scale;
                float fontSize = (float)letter.PointSize * scale;
                if (fontSize < MinFontSize)
                {
                    fontSize = DefaultFontScale * scale;
                }

                if (!fontCache.TryGetValue(fontSize, out var font))
                {
                    font = new SKFont { Size = fontSize };
                    fontCache[fontSize] = font;
                }

                canvas.DrawText(letter.Value, x, y, SKTextAlign.Left, font, paint);
            }
        }
        finally
        {
            foreach (var font in fontCache.Values)
            {
                font.Dispose();
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, PngQuality);
        return data.ToArray();
    }

    /// <summary>
    /// Renders a specific region of a PDF page to a PNG byte array.
    /// Useful for sending only a table or element region to a vision LLM.
    /// </summary>
    /// <param name="page">The PDF page containing the region.</param>
    /// <param name="region">The bounding box of the region to render, in PDF coordinates.</param>
    /// <param name="dpi">Target resolution in dots per inch. Defaults to 150.</param>
    /// <returns>A PNG-encoded byte array of the rendered region.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    public static byte[] RenderRegion(Page page, PdfRectangle region, int dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(page);

        float scale = dpi / PdfDpi;
        int pixelWidth = Math.Max(1, (int)(region.Width * scale));
        int pixelHeight = Math.Max(1, (int)(region.Height * scale));

        using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var fontCache = new Dictionary<float, SKFont>();
        try
        {
            foreach (var letter in page.Letters)
            {
                double lx = letter.Location.X;
                double ly = letter.Location.Y;

                if (lx < region.Left || lx > region.Right || ly < region.Bottom || ly > region.Top)
                {
                    continue;
                }

                // Translate so region's bottom-left maps to image (0,0)
                float x = (float)(lx - region.Left) * scale;
                float y = (float)(region.Top - ly) * scale;
                float fontSize = (float)letter.PointSize * scale;
                if (fontSize < MinFontSize)
                {
                    fontSize = DefaultFontScale * scale;
                }

                if (!fontCache.TryGetValue(fontSize, out var font))
                {
                    font = new SKFont { Size = fontSize };
                    fontCache[fontSize] = font;
                }

                canvas.DrawText(letter.Value, x, y, SKTextAlign.Left, font, paint);
            }
        }
        finally
        {
            foreach (var font in fontCache.Values)
            {
                font.Dispose();
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, PngQuality);
        return data.ToArray();
    }
}
