using SkiaSharp;
using System;
using System.Collections.Generic;
using UglyToad.PdfPig.Content;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// Renders word bounding boxes to an <see cref="SKBitmap"/> for use as
/// input to ONNX layout detection models.
/// </summary>
public static class PageImageRenderer
{
    /// <summary>
    /// Render word bounding boxes as filled rectangles on a white background.
    /// PDF coordinates (bottom-left origin, Y-up) are converted to image coordinates
    /// (top-left origin, Y-down).
    /// </summary>
    /// <param name="words">The words whose bounding boxes to render.</param>
    /// <param name="pageWidth">The page width in PDF units.</param>
    /// <param name="pageHeight">The page height in PDF units.</param>
    /// <param name="dpi">Rendering DPI. Higher values produce larger images with more detail.</param>
    /// <returns>A new bitmap with word bounding boxes rendered.</returns>
    public static SKBitmap RenderWords(IReadOnlyList<Word> words, double pageWidth, double pageHeight, int dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(words);

        // Scale from PDF points (72 dpi) to target DPI
        double scale = dpi / 72.0;
        int imageWidth = Math.Max(1, (int)(pageWidth * scale));
        int imageHeight = Math.Max(1, (int)(pageHeight * scale));

        var info = new SKImageInfo(imageWidth, imageHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Color = SKColors.Black;
        paint.Style = SKPaintStyle.Fill;

        // Compute the bounds offset so we render relative to (0, 0)
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        foreach (var word in words)
        {
            minX = Math.Min(minX, word.BoundingBox.Left);
            minY = Math.Min(minY, word.BoundingBox.Bottom);
        }

        foreach (var word in words)
        {
            var bb = word.BoundingBox;

            // Translate to origin
            double left = (bb.Left - minX) * scale;
            double right = (bb.Right - minX) * scale;
            double pdfBottom = (bb.Bottom - minY) * scale;
            double pdfTop = (bb.Top - minY) * scale;

            // Convert PDF Y (bottom-up) to image Y (top-down)
            float imgLeft = (float)left;
            float imgTop = (float)(imageHeight - pdfTop);
            float imgRight = (float)right;
            float imgBottom = (float)(imageHeight - pdfBottom);

            if (imgRight > imgLeft && imgBottom > imgTop)
            {
                canvas.DrawRect(new SKRect(imgLeft, imgTop, imgRight, imgBottom), paint);
            }
        }

        return bitmap;
    }
}
