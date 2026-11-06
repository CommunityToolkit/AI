// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime.Tensors;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal sealed record PageRaster(byte[] Bgra, int Width, int Height, double PageWidth, double PageHeight)
{
    internal static PageRaster Render(PdfDocument document, Page page, double scale, int maximumPixels, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var bitmap = PdfPageRenderer.Render(document, page.Number, scale, maximumPixels, token);
        int width = bitmap.Width;
        int height = bitmap.Height;

        if (!OnnxCompatibility.IsFinite(page.Width) || !OnnxCompatibility.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0 ||
            Math.Abs((double)width / height - page.Width / page.Height) > 0.02)
        {
            throw new InvalidDataException("The raster and native text disagree on the visible PDF page frame.");
        }

        byte[] bytes = PdfPageRenderer.CopyBgra(bitmap, token);

        token.ThrowIfCancellationRequested();
        return new(bytes, width, height, page.Width, page.Height);
    }

    internal DenseTensor<byte> ToTensor(CancellationToken token)
    {
        var tensor = new DenseTensor<byte>(new[] { 1, 3, 640, 640 });
        Span<byte> data = tensor.Buffer.Span;
        for (int y = 0; y < 640; y++)
        {
            token.ThrowIfCancellationRequested();
            double sy = OnnxCompatibility.Clamp((y + 0.5) * Height / 640 - 0.5, 0, Height - 1);
            int y0 = (int)sy;
            int y1 = Math.Min(y0 + 1, Height - 1);
            double fy = sy - y0;
            for (int x = 0; x < 640; x++)
            {
                double sx = OnnxCompatibility.Clamp((x + 0.5) * Width / 640 - 0.5, 0, Width - 1);
                int x0 = (int)sx;
                int x1 = Math.Min(x0 + 1, Width - 1);
                double fx = sx - x0;
                for (int c = 0; c < 3; c++)
                {
                    double upper = Channel(x0, y0, c) * (1 - fx) + Channel(x1, y0, c) * fx;
                    double lower = Channel(x0, y1, c) * (1 - fx) + Channel(x1, y1, c) * fx;
                    data[c * 640 * 640 + y * 640 + x] = (byte)OnnxCompatibility.Clamp(
                        Math.Round(upper * (1 - fy) + lower * fy), 0, 255);
                }
            }
        }

        return tensor;
    }

    internal PdfRectangle ToPageBounds(float left, float top, float right, float bottom)
    {
        // PdfPig applies the effective crop origin and page rotation to both paths.
        // Actual raster sizes absorb DPI rounding; only scale and Y-flip remain.
        double x1 = OnnxCompatibility.Clamp(left, 0, Width) * PageWidth / Width;
        double x2 = OnnxCompatibility.Clamp(right, 0, Width) * PageWidth / Width;
        double y1 = PageHeight - OnnxCompatibility.Clamp(bottom, 0, Height) * PageHeight / Height;
        double y2 = PageHeight - OnnxCompatibility.Clamp(top, 0, Height) * PageHeight / Height;
        return new(x1, y1, x2, y2);
    }

    private double Channel(int x, int y, int rgbChannel)
    {
        int index = (y * Width + x) * 4;
        double alpha = Bgra[index + 3] / 255d;
        return Bgra[index + 2 - rgbChannel] * alpha + 255 * (1 - alpha);
    }
}
