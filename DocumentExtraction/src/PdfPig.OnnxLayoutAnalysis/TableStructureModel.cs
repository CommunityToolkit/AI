// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal sealed class TableStructureModel : IDisposable
{
    internal const string ModelHash = "2C90A63298DF61006A45267932F47B345A8B104CE53FD504EACF11AEE3C05A41";
    private static readonly string[] s_labels =
        ["table", "table column", "table row", "table column header", "table projected row header", "table spanning cell", "no object"];
    private readonly InferenceSession _session;

    internal TableStructureModel(string path, SessionOptions? options)
    {
        LocalOnnxSession.Verify(path, ModelHash);
        _session = LocalOnnxSession.Create(path, options);
        try
        {
            LocalOnnxSession.Check(_session.InputMetadata, "pixel_values", [-1, -1, -1, -1]);
            LocalOnnxSession.Check(_session.OutputMetadata, "logits", [-1, -1, 7]);
            LocalOnnxSession.Check(_session.OutputMetadata, "pred_boxes", [-1, -1, 4]);
            if (_session.InputMetadata.Count != 1 || _session.OutputMetadata.Count != 2)
            {
                throw new InvalidDataException("Expected the pinned singleton v1.0 TATR export without a pixel_mask input.");
            }
        }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    internal PdfPigTableStructure Analyze(
        PageRaster raster, PdfPigLayoutRegion region, IReadOnlyList<TableTextToken> tokens, CancellationToken token)
    {
        var crop = Prepare(raster, region.Bounds, token);
        using var results = LocalOnnxSession.Run(_session, NamedOnnxValue.CreateFromTensor("pixel_values", crop.Input), token);
        var (proposals, accepted) = Decode(
            results.Single(output => output.Name == "logits").AsTensor<float>(),
            results.Single(output => output.Name == "pred_boxes").AsTensor<float>(), raster, crop, token);
        var grid = TableStructureDecoder.Build(accepted, tokens, region.Bounds, token);
        return new(region, proposals, grid);
    }

    internal static TableCrop Prepare(PageRaster raster, PdfRectangle bounds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Right) ||
            !double.IsFinite(bounds.Top) || !double.IsFinite(bounds.Bottom) || bounds.Width <= 0 || bounds.Height <= 0 ||
            raster.Width <= 0 || raster.Height <= 0 || !double.IsFinite(raster.PageWidth) || !double.IsFinite(raster.PageHeight) ||
            raster.PageWidth <= 0 || raster.PageHeight <= 0 ||
            (long)raster.Width * raster.Height > 32_000_000 || raster.Bgra.LongLength != (long)raster.Width * raster.Height * 4)
        {
            throw new InvalidDataException("Invalid TATR source raster or table crop bounds.");
        }

        int left = (int)Math.Clamp(Math.Floor(bounds.Left * raster.Width / raster.PageWidth), 0, raster.Width);
        int top = (int)Math.Clamp(Math.Floor((raster.PageHeight - bounds.Top) * raster.Height / raster.PageHeight), 0, raster.Height);
        int right = (int)Math.Clamp(Math.Ceiling(bounds.Right * raster.Width / raster.PageWidth), 0, raster.Width);
        int bottom = (int)Math.Clamp(Math.Ceiling((raster.PageHeight - bounds.Bottom) * raster.Height / raster.PageHeight), 0, raster.Height);
        int width = right - left, height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The selected TATR crop is outside the visible page.");
        }

        double scale = Math.Min(800d / Math.Min(width, height), 1000d / Math.Max(width, height));
        int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        var input = new DenseTensor<float>([1, 3, targetHeight, targetWidth]);
        double[] mean = [.485, .456, .406], deviation = [.229, .224, .225];
        for (int y = 0; y < targetHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            double sy = Math.Clamp((y + .5) * height / targetHeight - .5, 0, height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, height - 1);
            for (int x = 0; x < targetWidth; x++)
            {
                double sx = Math.Clamp((x + .5) * width / targetWidth - .5, 0, width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, width - 1);
                double fx = sx - x0, fy = sy - y0;
                for (int channel = 0; channel < 3; channel++)
                {
                    double upper = Pixel(left + x0, top + y0, channel) * (1 - fx) + Pixel(left + x1, top + y0, channel) * fx;
                    double lower = Pixel(left + x0, top + y1, channel) * (1 - fx) + Pixel(left + x1, top + y1, channel) * fx;
                    double value = Math.Clamp(Math.Round(upper * (1 - fy) + lower * fy), 0, 255);
                    input[0, channel, y, x] = (float)((value / 255 - mean[channel]) / deviation[channel]);
                }
            }
        }

        return new(input, left, top, width, height);

        double Pixel(int x, int y, int rgbChannel)
        {
            int index = (y * raster.Width + x) * 4;
            double alpha = raster.Bgra[index + 3] / 255d;
            return raster.Bgra[index + 2 - rgbChannel] * alpha + 255 * (1 - alpha);
        }
    }

    internal static (IReadOnlyList<PdfPigTableProposal> Proposals, IReadOnlyList<TableProposal> Accepted) Decode(
        Tensor<float> logits, Tensor<float> boxes, PageRaster raster, TableCrop crop, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!logits.Dimensions.SequenceEqual(new[] { 1, 125, 7 }) ||
            !boxes.Dimensions.SequenceEqual(new[] { 1, 125, 4 }))
        {
            throw new InvalidDataException("TATR returned unexpected singleton [1,125,7]/[1,125,4] dimensions.");
        }

        var raw = new List<PdfPigTableProposal>(125);
        var accepted = new List<TableProposal>();
        for (int query = 0; query < 125; query++)
        {
            token.ThrowIfCancellationRequested();
            var scores = new float[7];
            var box = new float[4];
            for (int index = 0; index < scores.Length; index++)
            {
                scores[index] = logits[0, query, index];
                if (!float.IsFinite(scores[index]))
                {
                    throw new InvalidDataException($"TATR query {query} has nonfinite logit {index}.");
                }
            }

            for (int index = 0; index < box.Length; index++)
            {
                box[index] = boxes[0, query, index];
                if (!float.IsFinite(box[index]) || (index >= 2 && box[index] <= 0))
                {
                    throw new InvalidDataException($"TATR query {query} has invalid center box coordinate {index}.");
                }
            }

            int label = 0;
            for (int index = 1; index < scores.Length; index++)
            {
                if (scores[index] > scores[label]) label = index;
            }

            double denominator = scores.Sum(score => Math.Exp((double)score - scores[label]));
            float confidence = (float)(1 / denominator);
            double x0 = Math.Clamp(((double)box[0] - box[2] / 2d) * crop.Width, 0, crop.Width);
            double y0 = Math.Clamp(((double)box[1] - box[3] / 2d) * crop.Height, 0, crop.Height);
            double x1 = Math.Clamp(((double)box[0] + box[2] / 2d) * crop.Width, 0, crop.Width);
            double y1 = Math.Clamp(((double)box[1] + box[3] / 2d) * crop.Height, 0, crop.Height);
            PdfRectangle? mapped = x1 > x0 && y1 > y0
                ? raster.ToPageBounds((float)(crop.Left + x0), (float)(crop.Top + y0), (float)(crop.Left + x1), (float)(crop.Top + y1))
                : null;
            raw.Add(new(query, s_labels[label], confidence, box, scores, mapped));
            if (label != 6 && confidence >= .5f && mapped is PdfRectangle bounds)
            {
                accepted.Add(new(label, confidence, bounds));
            }
        }

        return (raw.AsReadOnly(), accepted.AsReadOnly());
    }

    public void Dispose() => _session.Dispose();
}

internal sealed record TableCrop(DenseTensor<float> Input, int Left, int Top, int Width, int Height);
