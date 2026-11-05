// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SkiaSharp;
using UglyToad.PdfPig;

namespace CommunityToolkit.DocumentExtraction.Vision;

internal static class PdfPageRasterizer
{
    internal static byte[] RenderPage(PdfDocument document, int number, CancellationToken token = default)
    {
        using var bitmap = PdfPageRenderer.Render(document, number, 2d, 32_000_000, token);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("SkiaSharp could not encode the rendered PDF page.");
        token.ThrowIfCancellationRequested();
        return data.ToArray();
    }
}
