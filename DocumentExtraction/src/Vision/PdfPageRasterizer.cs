// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

namespace CommunityToolkit.DocumentExtraction.Vision;

internal static class PdfPageRasterizer
{
    public static byte[] RenderPage(byte[] bytes, int pageNumber)
    {
        using var documentReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(2d));
        using var pageReader = documentReader.GetPageReader(pageNumber - 1);
        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();
        byte[] pixels = pageReader.GetImage();
        if (pixels.Length != checked(width * height * 4))
        {
            throw new InvalidDataException("PDFium returned an invalid BGRA image length.");
        }

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("SkiaSharp could not encode the rendered PDF page.");
        return encoded.ToArray();
    }
}
