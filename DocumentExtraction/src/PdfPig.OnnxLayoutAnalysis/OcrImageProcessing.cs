// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Clipper2Lib;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal readonly record struct RasterPoint(double X, double Y);

internal sealed record OcrQuad(IReadOnlyList<RasterPoint> Points, float Score);

internal sealed record RectifiedCrop(
    byte[] Bgr,
    int Width,
    int Height,
    IReadOnlyList<double> RasterToCrop,
    IReadOnlyList<RasterPoint> SourceQuad);

/// <summary>Bounded, managed PP-OCRv5 image preparation and DB region geometry; no inference or word geometry.</summary>
internal static class OcrImageProcessing
{
    private const int MaximumPixels = 32_000_000;
    private const int MaximumRecognizerWidth = 4096;
    private const int MaximumContours = 1000;
    private const int MaximumBoundaryEdges = 262_144;
    private const int MaximumContourPoints = 16_384;
    private const int MaximumHullPoints = 2048;
    private const long MaximumGeometryWork = 16_000_000;
    private const long MaximumScorePixels = 64_000_000;

    internal static DenseTensor<float> PrepareDetector(PageRaster raster, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateRaster(raster);
        double ratio = Math.Min(1, 960d / Math.Max(raster.Width, raster.Height));
        int width = RoundDetectorDimension((int)(raster.Width * ratio));
        int height = RoundDetectorDimension((int)(raster.Height * ratio));
        return ResizeNormalized(raster.Bgra, raster.Width, raster.Height, true, width, height, width, token);
    }

    internal static IReadOnlyList<OcrQuad> DecodeDetector(
        Tensor<float> probabilities,
        int originalWidth,
        int originalHeight,
        int resizedWidth,
        int resizedHeight,
        CancellationToken token,
        OcrProbabilityAudit? audit = null)
    {
        token.ThrowIfCancellationRequested();
        ValidateSize(originalWidth, originalHeight, "DB original raster");
        if (resizedWidth <= 0 || resizedHeight <= 0 || resizedWidth > 960 || resizedHeight > 960 ||
            probabilities.Rank != 4 || probabilities.Dimensions[0] != 1 || probabilities.Dimensions[1] != 1 ||
            probabilities.Dimensions[2] != resizedHeight || probabilities.Dimensions[3] != resizedWidth)
        {
            throw new InvalidDataException("DB probabilities must have shape [1,1,resizedHeight,resizedWidth], bounded by 960.");
        }

        var bitmap = new bool[resizedWidth * resizedHeight];
        for (int y = 0; y < resizedHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < resizedWidth; x++)
            {
                float value;
                try
                {
                    value = (audit ??= new OcrProbabilityAudit()).Normalize(
                        probabilities[0, 0, y, x], y * resizedWidth + x);
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        $"DB probability at ({x},{y}) is not finite and in [0,1] within the supported one-ULP upper-bound tolerance.",
                        exception);
                }

                if (value > .3f)
                {
                    // A 2x2 kernel anchored at (1,1) expands foreground to its right and bottom.
                    bitmap[y * resizedWidth + x] = true;
                    if (x + 1 < resizedWidth)
                    {
                        bitmap[y * resizedWidth + x + 1] = true;
                    }

                    if (y + 1 < resizedHeight)
                    {
                        bitmap[(y + 1) * resizedWidth + x] = true;
                        if (x + 1 < resizedWidth)
                        {
                            bitmap[(y + 1) * resizedWidth + x + 1] = true;
                        }
                    }
                }
            }
        }

        var edges = BoundaryEdges(bitmap, resizedWidth, resizedHeight, token);
        var outgoing = new Dictionary<long, List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            if ((i & 1023) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            if (!outgoing.TryGetValue(edges[i].Start, out var indices))
            {
                outgoing.Add(edges[i].Start, indices = []);
            }

            indices.Add(i);
        }

        var visited = new bool[edges.Count];
        var results = new List<OcrQuad>();
        int contourCount = 0;
        long geometryWork = 0;
        long scorePixels = 0;
        for (int seed = 0; seed < edges.Count; seed++)
        {
            token.ThrowIfCancellationRequested();
            if (visited[seed])
            {
                continue;
            }

            if (++contourCount > MaximumContours)
            {
                throw new InvalidDataException("DB contour budget exceeded (1000); no regions have been silently dropped.");
            }

            List<RasterPoint> contour = [];
            int current = seed;
            do
            {
                token.ThrowIfCancellationRequested();
                if (visited[current])
                {
                    throw new InvalidDataException("DB boundary traversal encountered a non-closed contour.");
                }

                if (contour.Count == MaximumContourPoints)
                {
                    throw new InvalidDataException("DB contour point budget exceeded (16384).");
                }

                visited[current] = true;
                var edge = edges[current];
                // Pixel centers, not pixel-cell vertices, match minAreaRect contour coordinates.
                contour.Add(edge.Foreground);
                var candidates = outgoing[edge.End];
                int next = -1;
                int bestTurn = int.MaxValue;
                foreach (int candidate in candidates)
                {
                    if (visited[candidate] && candidate != seed)
                    {
                        continue;
                    }

                    int turn = (edges[candidate].Direction - edge.Direction + 4) % 4;
                    // At diagonal saddles join 8-connected foreground, retaining separate hole loops.
                    int priority = turn switch { 3 => 0, 0 => 1, 1 => 2, _ => 3 };
                    if (priority < bestTurn)
                    {
                        bestTurn = priority;
                        next = candidate;
                    }
                }

                if (next < 0)
                {
                    throw new InvalidDataException("DB boundary traversal could not close a contour.");
                }

                current = next;
            }
            while (current != seed);

            var rectangle = MinimumRectangle(contour, ref geometryWork, token);
            if (rectangle.Length == 0 || MinimumSide(rectangle) < 3)
            {
                continue;
            }

            float score = RectangleScore(probabilities, rectangle, resizedWidth, resizedHeight, ref scorePixels, token);
            if (score < .5f)
            {
                continue;
            }

            double area = Math.Abs(SignedArea(rectangle));
            double perimeter = Enumerable.Range(0, 4).Sum(i => Distance(rectangle[i], rectangle[(i + 1) % 4]));
            var path = new PathD(rectangle.Select(p => new PointD(p.X, p.Y)));
            token.ThrowIfCancellationRequested();
            // Clipper2 is a managed Boost-1.0 polygon offset, not a bounding-box inflation.
            var expanded = Clipper.InflatePaths(new PathsD { path }, area * 1.6 / perimeter,
                JoinType.Round, EndType.Polygon, 2, 4);
            token.ThrowIfCancellationRequested();
            if (expanded.Count != 1)
            {
                continue;
            }

            if (expanded[0].Count > MaximumContourPoints)
            {
                throw new InvalidDataException("DB offset polygon point budget exceeded.");
            }

            var box = MinimumRectangle(expanded[0].Select(p => new RasterPoint(p.x, p.y)).ToList(),
                ref geometryWork, token);
            if (box.Length == 0 || MinimumSide(box) < 5)
            {
                continue;
            }

            var source = OrderQuad(box.Select(p => new RasterPoint(
                Math.Clamp(p.X * originalWidth / resizedWidth, 0, originalWidth - 1),
                Math.Clamp(p.Y * originalHeight / resizedHeight, 0, originalHeight - 1))).ToArray());
            results.Add(new(Array.AsReadOnly(source), score));
        }

        return results.AsReadOnly();
    }

    internal static RectifiedCrop Rectify(PageRaster raster, OcrQuad quad, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateRaster(raster);
        if (quad.Points.Count != 4 || quad.Points.Any(p =>
            !double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > int.MaxValue || Math.Abs(p.Y) > int.MaxValue))
        {
            throw new InvalidDataException("OCR rectification requires four finite, bounded source corners.");
        }

        var source = OrderQuad(quad.Points.ToArray());
        for (int i = 0; i < 4; i++)
        {
            if (Cross(source[i], source[(i + 1) % 4], source[(i + 2) % 4]) <= 1e-8)
            {
                throw new InvalidDataException("OCR rectification requires a nondegenerate convex quadrilateral.");
            }
        }

        double rawWidth = Math.Round(Math.Max(Distance(source[0], source[1]), Distance(source[2], source[3])));
        double rawHeight = Math.Round(Math.Max(Distance(source[0], source[3]), Distance(source[1], source[2])));
        if (rawWidth < 1 || rawHeight < 1 || rawWidth > MaximumPixels || rawHeight > MaximumPixels)
        {
            throw new InvalidDataException("OCR rectification crop dimensions are empty or over budget.");
        }

        int width = (int)rawWidth;
        int height = (int)rawHeight;
        ValidateSize(width, height, "OCR rectification crop");
        RecognizerWidth(width, height);
        RasterPoint[] destination = [new(0, 0), new(width, 0), new(width, height), new(0, height)];
        double[] transform = Homography(source, destination);
        double[] inverse = Homography(destination, source);
        byte[] bgr = new byte[checked(width * height * 3)];
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                if ((x & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                double divisor = inverse[6] * x + inverse[7] * y + inverse[8];
                if (!double.IsFinite(divisor) || Math.Abs(divisor) < 1e-12)
                {
                    throw new InvalidDataException("OCR perspective transform crosses infinity inside the crop.");
                }

                double sx = (inverse[0] * x + inverse[1] * y + inverse[2]) / divisor;
                double sy = (inverse[3] * x + inverse[4] * y + inverse[5]) / divisor;
                for (int channel = 0; channel < 3; channel++)
                {
                    bgr[(y * width + x) * 3 + channel] = CubicSample(raster, sx, sy, channel);
                }
            }
        }

        return new(bgr, width, height, Array.AsReadOnly(transform), Array.AsReadOnly(source));
    }

    internal static DenseTensor<float> PrepareRecognizer(RectifiedCrop crop, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateSize(crop.Width, crop.Height, "OCR recognizer crop");
        int width = RecognizerWidth(crop.Width, crop.Height);
        if (crop.Bgr.LongLength != (long)crop.Width * crop.Height * 3)
        {
            throw new InvalidDataException("OCR crop buffer does not match its BGR dimensions.");
        }

        int resizedWidth = Math.Min(width, (int)Math.Ceiling(48d * crop.Width / crop.Height));
        return ResizeNormalized(crop.Bgr, crop.Width, crop.Height, false, resizedWidth, 48, width, token);
    }

    private static int RoundDetectorDimension(int dimension) => Math.Max(32, (int)Math.Round(dimension / 32d) * 32);

    private static int RecognizerWidth(int width, int height)
    {
        double requested = Math.Max(320, Math.Floor(48d * width / height));
        if (requested > MaximumRecognizerWidth)
        {
            throw new InvalidDataException("OCR recognizer width budget exceeded (4096); the crop will not be clamped.");
        }

        return (int)requested;
    }

    private static void ValidateSize(int width, int height, string context)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
        {
            throw new InvalidDataException($"{context} exceeds the 32000000-pixel budget or has nonpositive dimensions.");
        }
    }

    private static void ValidateRaster(PageRaster raster)
    {
        ValidateSize(raster.Width, raster.Height, "OCR source raster");
        if (raster.Bgra.LongLength != (long)raster.Width * raster.Height * 4)
        {
            throw new InvalidDataException("OCR source raster buffer does not match its BGRA dimensions.");
        }
    }

    private static DenseTensor<float> ResizeNormalized(
        byte[] bytes, int sourceWidth, int sourceHeight, bool bgra,
        int width, int height, int paddedWidth, CancellationToken token)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, paddedWidth });
        Span<float> output = tensor.Buffer.Span;
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            double sy = Math.Clamp((y + .5) * sourceHeight / height - .5, 0, sourceHeight - 1);
            int y0 = (int)sy;
            int y1 = Math.Min(y0 + 1, sourceHeight - 1);
            double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Clamp((x + .5) * sourceWidth / width - .5, 0, sourceWidth - 1);
                int x0 = (int)sx;
                int x1 = Math.Min(x0 + 1, sourceWidth - 1);
                double fx = sx - x0;
                for (int channel = 0; channel < 3; channel++)
                {
                    double upper = Channel(bytes, sourceWidth, x0, y0, channel, bgra) * (1 - fx) +
                        Channel(bytes, sourceWidth, x1, y0, channel, bgra) * fx;
                    double lower = Channel(bytes, sourceWidth, x0, y1, channel, bgra) * (1 - fx) +
                        Channel(bytes, sourceWidth, x1, y1, channel, bgra) * fx;
                    byte value = ToByte(upper * (1 - fy) + lower * fy);
                    output[channel * height * paddedWidth + y * paddedWidth + x] = (value / 255f - .5f) / .5f;
                }
            }
        }

        // DenseTensor initializes the unused right-hand columns to normalized zero, not white.
        return tensor;
    }

    private static byte Channel(byte[] bytes, int width, int x, int y, int channel, bool bgra)
    {
        int index = (y * width + x) * (bgra ? 4 : 3);
        if (!bgra)
        {
            return bytes[index + channel];
        }

        int alpha = bytes[index + 3];
        return (byte)((bytes[index + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private readonly record struct BoundaryEdge(long Start, long End, int Direction, RasterPoint Foreground);

    private static List<BoundaryEdge> BoundaryEdges(bool[] bitmap, int width, int height, CancellationToken token)
    {
        List<BoundaryEdge> edges = [];
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                if (!bitmap[y * width + x])
                {
                    continue;
                }

                if (y == 0 || !bitmap[(y - 1) * width + x])
                {
                    Add(x, y, x + 1, y, 0, x, y);
                }

                if (x == width - 1 || !bitmap[y * width + x + 1])
                {
                    Add(x + 1, y, x + 1, y + 1, 1, x, y);
                }

                if (y == height - 1 || !bitmap[(y + 1) * width + x])
                {
                    Add(x + 1, y + 1, x, y + 1, 2, x, y);
                }

                if (x == 0 || !bitmap[y * width + x - 1])
                {
                    Add(x, y + 1, x, y, 3, x, y);
                }
            }
        }

        return edges;

        void Add(int x0, int y0, int x1, int y1, int direction, int x, int y)
        {
            if (edges.Count == MaximumBoundaryEdges)
            {
                throw new InvalidDataException("DB boundary edge budget exceeded (262144).");
            }

            edges.Add(new((long)y0 * (width + 1) + x0, (long)y1 * (width + 1) + x1, direction, new(x, y)));
        }
    }

    private static RasterPoint[] MinimumRectangle(List<RasterPoint> points, ref long work, CancellationToken token)
    {
        if (points.Count > MaximumContourPoints)
        {
            throw new InvalidDataException("DB convex-hull input point budget exceeded.");
        }

        token.ThrowIfCancellationRequested();
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3)
        {
            return [];
        }

        var hull = new List<RasterPoint>();
        foreach (var point in sorted)
        {
            token.ThrowIfCancellationRequested();
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        int lowerCount = hull.Count;
        for (int i = sorted.Length - 2; i >= 0; i--)
        {
            token.ThrowIfCancellationRequested();
            while (hull.Count > lowerCount && Cross(hull[^2], hull[^1], sorted[i]) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(sorted[i]);
        }

        hull.RemoveAt(hull.Count - 1);
        if (hull.Count < 3)
        {
            return [];
        }

        if (hull.Count > MaximumHullPoints || (work += (long)hull.Count * hull.Count) > MaximumGeometryWork)
        {
            throw new InvalidDataException("DB convex-hull/minimum-rectangle iteration budget exceeded.");
        }

        double bestArea = double.PositiveInfinity;
        RasterPoint[] result = [];
        for (int i = 0; i < hull.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            double length = Distance(a, b);
            double ux = (b.X - a.X) / length;
            double uy = (b.Y - a.Y) / length;
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            foreach (var point in hull)
            {
                double u = point.X * ux + point.Y * uy;
                double v = -point.X * uy + point.Y * ux;
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }

            double area = (maxU - minU) * (maxV - minV);
            if (area < bestArea - 1e-8)
            {
                bestArea = area;
                result = [ToPoint(minU, minV), ToPoint(maxU, minV), ToPoint(maxU, maxV), ToPoint(minU, maxV)];
            }

            RasterPoint ToPoint(double u, double v) => new(u * ux - v * uy, u * uy + v * ux);
        }

        return OrderQuad(result);
    }

    private static RasterPoint[] OrderQuad(RasterPoint[] points)
    {
        double cx = points.Average(p => p.X);
        double cy = points.Average(p => p.Y);
        var ordered = points.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToArray();
        int first = 0;
        for (int i = 1; i < ordered.Length; i++)
        {
            double difference = ordered[i].X + ordered[i].Y - ordered[first].X - ordered[first].Y;
            if (difference < -1e-8 || (Math.Abs(difference) <= 1e-8 &&
                (ordered[i].Y < ordered[first].Y || (ordered[i].Y == ordered[first].Y && ordered[i].X < ordered[first].X))))
            {
                first = i;
            }
        }

        return Enumerable.Range(0, ordered.Length).Select(i => ordered[(first + i) % ordered.Length]).ToArray();
    }

    private static float RectangleScore(Tensor<float> probabilities, RasterPoint[] rectangle, int width, int height,
        ref long work, CancellationToken token)
    {
        var rounded = rectangle.Select(p => new RasterPoint(Math.Round(p.X), Math.Round(p.Y))).ToArray();
        int minY = Math.Clamp((int)rounded.Min(p => p.Y), 0, height - 1);
        int maxY = Math.Clamp((int)rounded.Max(p => p.Y), 0, height - 1);
        int minX = Math.Clamp((int)rounded.Min(p => p.X), 0, width - 1);
        int maxX = Math.Clamp((int)rounded.Max(p => p.X), 0, width - 1);
        if ((work += (long)(maxY - minY + 1) * (maxX - minX + 1)) > MaximumScorePixels)
        {
            throw new InvalidDataException("DB rectangle probability-mask pixel budget exceeded.");
        }

        var left = Enumerable.Repeat(int.MaxValue, maxY - minY + 1).ToArray();
        var right = Enumerable.Repeat(int.MinValue, maxY - minY + 1).ToArray();
        // Rasterize inclusive integer edges, then fill convex row spans (the original probability mask).
        for (int i = 0; i < 4; i++)
        {
            int x = (int)rounded[i].X, y = (int)rounded[i].Y;
            int x1 = (int)rounded[(i + 1) % 4].X, y1 = (int)rounded[(i + 1) % 4].Y;
            int dx = Math.Abs(x1 - x), sx = x < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y), sy = y < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (y >= minY && y <= maxY)
                {
                    left[y - minY] = Math.Min(left[y - minY], x);
                    right[y - minY] = Math.Max(right[y - minY], x);
                }

                if (x == x1 && y == y1)
                {
                    break;
                }

                int twice = 2 * error;
                if (twice >= dy)
                {
                    error += dy;
                    x += sx;
                }

                if (twice <= dx)
                {
                    error += dx;
                    y += sy;
                }
            }
        }

        double sum = 0;
        int count = 0;
        for (int y = minY; y <= maxY; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = Math.Max(0, left[y - minY]); x <= Math.Min(width - 1, right[y - minY]); x++)
            {
                // The full map was validated before contour work. Reuse only the proven one-ULP
                // upper-bound normalization, without mutating raw output or counting it twice.
                sum += MathF.Min(probabilities[0, 0, y, x], 1f);
                count++;
            }
        }

        return count == 0 ? 0 : (float)(sum / count);
    }

    private static double MinimumSide(RasterPoint[] rectangle) =>
        Math.Min(Distance(rectangle[0], rectangle[1]), Distance(rectangle[1], rectangle[2]));

    private static double Distance(RasterPoint a, RasterPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Cross(RasterPoint a, RasterPoint b, RasterPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double SignedArea(RasterPoint[] polygon) =>
        Enumerable.Range(0, polygon.Length).Sum(i =>
            polygon[i].X * polygon[(i + 1) % polygon.Length].Y -
            polygon[(i + 1) % polygon.Length].X * polygon[i].Y) / 2;

    private static double[] Homography(RasterPoint[] source, RasterPoint[] destination)
    {
        var equations = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = source[i].X, y = source[i].Y, u = destination[i].X, v = destination[i].Y;
            equations[2 * i, 0] = x;
            equations[2 * i, 1] = y;
            equations[2 * i, 2] = 1;
            equations[2 * i, 6] = -u * x;
            equations[2 * i, 7] = -u * y;
            equations[2 * i, 8] = u;
            equations[2 * i + 1, 3] = x;
            equations[2 * i + 1, 4] = y;
            equations[2 * i + 1, 5] = 1;
            equations[2 * i + 1, 6] = -v * x;
            equations[2 * i + 1, 7] = -v * y;
            equations[2 * i + 1, 8] = v;
        }

        for (int column = 0; column < 8; column++)
        {
            int pivot = column;
            for (int row = column + 1; row < 8; row++)
            {
                if (Math.Abs(equations[row, column]) > Math.Abs(equations[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(equations[pivot, column]) < 1e-12)
            {
                throw new InvalidDataException("OCR perspective homography is singular.");
            }

            for (int c = 0; c < 9; c++)
            {
                (equations[column, c], equations[pivot, c]) = (equations[pivot, c], equations[column, c]);
            }

            double divisor = equations[column, column];
            for (int c = column; c < 9; c++)
            {
                equations[column, c] /= divisor;
            }

            for (int row = 0; row < 8; row++)
            {
                if (row == column)
                {
                    continue;
                }

                double factor = equations[row, column];
                for (int c = column; c < 9; c++)
                {
                    equations[row, c] -= factor * equations[column, c];
                }
            }
        }

        double[] result = new double[9];
        for (int i = 0; i < 8; i++)
        {
            result[i] = equations[i, 8];
        }

        result[8] = 1;
        if (result.Any(v => !double.IsFinite(v)))
        {
            throw new InvalidDataException("OCR perspective homography is not finite.");
        }

        return result;
    }

    private static byte CubicSample(PageRaster raster, double x, double y, int channel)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new InvalidDataException("OCR perspective sampling coordinate is not finite.");
        }

        // OpenCV INTER_CUBIC's a=-.75 kernel and 1/32-pixel phases, with BORDER_REPLICATE.
        // Evaluate coefficients in double rather than OpenCV's platform-specific fixed-point SIMD.
        x = Math.Round(Math.Clamp(x, -2, raster.Width + 1d) * 32) / 32;
        y = Math.Round(Math.Clamp(y, -2, raster.Height + 1d) * 32) / 32;
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double value = 0;
        for (int dy = -1; dy <= 2; dy++)
        {
            double wy = Cubic(y - (iy + dy));
            for (int dx = -1; dx <= 2; dx++)
            {
                value += Channel(raster.Bgra, raster.Width,
                    Math.Clamp(ix + dx, 0, raster.Width - 1),
                    Math.Clamp(iy + dy, 0, raster.Height - 1), channel, true) *
                    Cubic(x - (ix + dx)) * wy;
            }
        }

        return ToByte(value);
    }

    private static double Cubic(double distance)
    {
        double t = Math.Abs(distance);
        return t <= 1 ? (1.25 * t - 2.25) * t * t + 1 :
            t < 2 ? ((-.75 * t + 3.75) * t - 6) * t + 3 : 0;
    }
}
