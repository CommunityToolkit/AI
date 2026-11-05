// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace CommunityToolkit.DocumentExtraction;

internal static class PdfPageRenderer
{
    internal static PdfDocument Open(byte[] bytes)
    {
        var document = PdfDocument.Open(bytes, new ParsingOptions
        {
            FilterProvider = SkiaRenderingFilterProvider.Instance,
        });
        try
        {
            document.AddSkiaPageFactory();
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    internal static SKBitmap Render(PdfDocument document, int number, double scale, int maximumPixels, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = document.GetPageSize(number);
        double width = Math.Ceiling(page.Width * scale), height = Math.Ceiling(page.Height * scale);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            width > int.MaxValue || height > int.MaxValue || width * height > maximumPixels)
        {
            throw new InvalidDataException("Rendered PDF page exceeds the configured pixel limit or has invalid dimensions.");
        }

        using var picture = document.GetPageAsSKPicture(number, token);
        token.ThrowIfCancellationRequested();
        var bitmap = new SKBitmap(new SKImageInfo((int)width, (int)height, SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            if (bitmap.GetPixels() == IntPtr.Zero)
            {
                throw new InvalidOperationException("SkiaSharp could not allocate the rendered PDF page.");
            }

            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.Scale((float)scale);
            canvas.DrawPicture(picture);
            token.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static byte[] CopyBgra(SKBitmap bitmap, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // ReadPixels converts color order, alpha representation and padded source rows.
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var normalized = new SKBitmap(info);
        using var image = SKImage.FromBitmap(bitmap);
        if (!image.ReadPixels(info, normalized.GetPixels(), normalized.RowBytes, 0, 0))
        {
            throw new InvalidDataException("SkiaSharp could not normalize the rendered PDF pixels.");
        }

        int rowBytes = checked(bitmap.Width * 4);
        byte[] bytes = new byte[checked(rowBytes * bitmap.Height)];
        for (int y = 0; y < bitmap.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            Marshal.Copy(IntPtr.Add(normalized.GetPixels(), checked(y * normalized.RowBytes)), bytes, y * rowBytes, rowBytes);
        }

        return bytes;
    }
}
