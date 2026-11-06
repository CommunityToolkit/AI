// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime.Tensors;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

/// <summary>Analytical synthetic image/geometry tests, not model inference or recognition accuracy claims.</summary>
public sealed class OcrImageProcessingTests
{
    [Theory]
    [InlineData(1, 1, 32, 32)]
    [InlineData(48, 80, 64, 64)]
    [InlineData(80, 112, 64, 128)]
    [InlineData(1275, 1650, 736, 960)]
    [InlineData(1000, 1, 960, 32)]
    [InlineData(961, 500, 960, 512)]
    public void DetectorUsesDiagnosticMax960ThenIntegerAndNearest32TiesEven(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        var tensor = OcrImageProcessing.PrepareDetector(Raster(width, height, (_, _) => (10, 20, 30, 255)), default);
        Assert.Equal(new[] { 1, 3, expectedHeight, expectedWidth }, tensor.Dimensions.ToArray());
        Assert.Equal(Normalize(10), tensor[0, 0, 0, 0]);
        Assert.Equal(Normalize(20), tensor[0, 1, expectedHeight - 1, expectedWidth - 1]);
        Assert.Equal(Normalize(30), tensor[0, 2, 0, 0]);
    }

    [Fact]
    public void DetectorCompositesAlphaOnWhiteAndKeepsBgrNotRgb()
    {
        var raster = Raster(32, 32, (x, _) => x < 16 ? (10, 20, 30, 128) : (0, 0, 0, 0));
        var tensor = OcrImageProcessing.PrepareDetector(raster, default);
        Assert.Equal(Normalize(132), tensor[0, 0, 0, 0]);
        Assert.Equal(Normalize(137), tensor[0, 1, 0, 0]);
        Assert.Equal(Normalize(142), tensor[0, 2, 0, 0]);
        Assert.Equal(1, tensor[0, 0, 31, 31]);
        Assert.Equal(1, tensor[0, 1, 31, 31]);
        Assert.Equal(1, tensor[0, 2, 31, 31]);
        Assert.Equal(new byte[] { 10, 20, 30, 128 }, raster.Bgra.Take(4));
    }

    [Fact]
    public void DetectorBilinearUsesHalfPixelCoordinatesAndRoundsToBytesBeforeNormalize()
    {
        var tensor = OcrImageProcessing.PrepareDetector(
            Raster(2, 1, (x, _) => (x == 0 ? 0 : 255, 40, 100, 255)), default);
        Assert.Equal(Normalize(0), tensor[0, 0, 0, 0]);
        Assert.Equal(Normalize(8), tensor[0, 0, 10, 8]);
        Assert.Equal(Normalize(120), tensor[0, 0, 10, 15]);
        Assert.Equal(Normalize(135), tensor[0, 0, 10, 16]);
        Assert.Equal(Normalize(247), tensor[0, 0, 10, 23]);
        Assert.Equal(Normalize(255), tensor[0, 0, 31, 31]);
        Assert.Equal(Normalize(40), tensor[0, 1, 10, 15]);
        Assert.Equal(Normalize(100), tensor[0, 2, 10, 15]);
    }

    [Fact]
    public void RecognizerNormalizesBgrThenRightPadsWithZeroNotWhite()
    {
        var crop = Crop(2, 1, [0, 40, 100, 255, 40, 100]);
        var tensor = OcrImageProcessing.PrepareRecognizer(crop, default);
        Assert.Equal(new[] { 1, 3, 48, 320 }, tensor.Dimensions.ToArray());
        Assert.Equal(Normalize(3), tensor[0, 0, 0, 24]);
        Assert.Equal(Normalize(125), tensor[0, 0, 25, 47]);
        Assert.Equal(Normalize(130), tensor[0, 0, 25, 48]);
        Assert.Equal(Normalize(252), tensor[0, 0, 47, 71]);
        Assert.Equal(Normalize(40), tensor[0, 1, 0, 95]);
        Assert.Equal(Normalize(100), tensor[0, 2, 47, 95]);
        for (int channel = 0; channel < 3; channel++)
        {
            for (int y = 0; y < 48; y++)
            {
                for (int x = 96; x < 320; x++)
                {
                    Assert.Equal(0, tensor[0, channel, y, x]);
                }
            }
        }

        Assert.Equal(new byte[] { 0, 40, 100, 255, 40, 100 }, crop.Bgr);
    }

    [Theory]
    [InlineData(441, 48, 441, 441)]
    [InlineData(101, 7, 692, 692)]
    [InlineData(1, 100, 320, 1)]
    [InlineData(256, 3, 4096, 4096)]
    public void RecognizerUsesHeight48TruncatedDynamicTargetAndCeilingResize(
        int width, int height, int targetWidth, int resizedWidth)
    {
        var crop = Crop(width, height, Enumerable.Repeat((byte)255, width * height * 3).ToArray());
        var tensor = OcrImageProcessing.PrepareRecognizer(crop, default);
        Assert.Equal(new[] { 1, 3, 48, targetWidth }, tensor.Dimensions.ToArray());
        Assert.Equal(1, tensor[0, 2, 47, resizedWidth - 1]);
        if (resizedWidth < targetWidth)
        {
            Assert.Equal(0, tensor[0, 0, 0, resizedWidth]);
            Assert.Equal(0, tensor[0, 2, 47, targetWidth - 1]);
        }
    }

    [Fact]
    public void DbEmptyAndNoAcceptedProbabilityMapsDoNotFabricateTextRegions()
    {
        Assert.Empty(Decode(Map(32, 32, (_, _) => 0)));
        Assert.Empty(Decode(Map(32, 32, (_, _) => .3f)));
        Assert.Empty(Decode(Map(32, 32, (_, _) => .49f)));
        Assert.Empty(Decode(Map(32, 32, (x, y) => x == 10 && y == 10 ? 1 : 0)));
    }

    [Fact]
    public void DbUsesOriginalProbabilitiesAndInclusiveRectangleScoreAtHalfThreshold()
    {
        var accepted = Assert.Single(Decode(Map(32, 32, (_, _) => .5f)));
        Assert.Equal(.5f, accepted.Score);
        Assert.Equal(new RasterPoint(0, 0), accepted.Points[0]);
        Assert.Equal(new RasterPoint(31, 0), accepted.Points[1]);
        Assert.Equal(new RasterPoint(31, 31), accepted.Points[2]);
        Assert.Equal(new RasterPoint(0, 31), accepted.Points[3]);
        var rectangle = Assert.Single(Decode(Map(64, 64,
            (x, y) => x >= 20 && x <= 39 && y >= 25 && y <= 34 ? .9f : 0)));
        Assert.Equal(.9f * 200 / 231, rectangle.Score, 6);
    }

    [Fact]
    public void DbRoundPolygonOffsetUsesAreaOverPerimeterNotRectangularAxisInflation()
    {
        var quad = Assert.Single(Decode(Map(64, 64,
            (x, y) => x >= 20 && x <= 39 && y >= 25 && y <= 34 ? 1 : 0)));
        // Dilation changes center-coordinate minrect from 19x9 to 20x10.
        // Round polygon offset distance = 200 * 1.6 / 60 = 16/3.
        double distance = 16d / 3;
        Near(new(20 - distance, 25 - distance), quad.Points[0], .01);
        Near(new(40 + distance, 25 - distance), quad.Points[1], .01);
        Near(new(40 + distance, 35 + distance), quad.Points[2], .01);
        Near(new(20 - distance, 35 + distance), quad.Points[3], .01);
        Assert.InRange(quad.Score, .86f, .87f);
    }

    [Fact]
    public void DbRestoresIndependentActualXAndYScalesAndClipsOnlyRasterBounds()
    {
        var map = Map(64, 64, (x, y) => x >= 20 && x <= 39 && y >= 25 && y <= 34 ? 1 : 0);
        var detector = Assert.Single(Decode(map));
        var original = Assert.Single(OcrImageProcessing.DecodeDetector(map, 127, 191, 64, 64, default));
        for (int i = 0; i < 4; i++)
        {
            Near(new(detector.Points[i].X * 127 / 64, detector.Points[i].Y * 191 / 64),
                original.Points[i], .0001);
        }

        var clipped = Assert.Single(OcrImageProcessing.DecodeDetector(
            Map(64, 64, (_, _) => 1), 127, 191, 64, 64, default));
        Assert.Equal(new RasterPoint(0, 0), clipped.Points[0]);
        Assert.Equal(new RasterPoint(126, 190), clipped.Points[2]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void DbRetainsRotatedAndDiagonalRegionGeometry(int direction)
    {
        var map = Map(96, 96, (x, y) =>
        {
            double u = ((x - 48) + direction * (y - 48)) / Math.Sqrt(2);
            double v = (-direction * (x - 48) + y - 48) / Math.Sqrt(2);
            return Math.Abs(u) <= 23 && Math.Abs(v) <= 6 ? 1 : 0;
        });
        var quad = Assert.Single(Decode(map));
        Assert.True(quad.Score >= .5f);
        for (int i = 0; i < 4; i++)
        {
            var a = quad.Points[i];
            var b = quad.Points[(i + 1) % 4];
            Assert.True(Math.Abs(a.X - b.X) > 5);
            Assert.True(Math.Abs(a.Y - b.Y) > 5);
            Assert.InRange(Math.Abs(Math.Abs(a.X - b.X) - Math.Abs(a.Y - b.Y)), 0, .05);
        }

        Assert.InRange(quad.Points.Average(p => p.X), 48.4, 48.6);
        Assert.InRange(quad.Points.Average(p => p.Y), 48.4, 48.6);
        // Actual round polygon inflation extends along rotated edge normals, not just X and Y.
        double area = Area(quad.Points);
        Assert.InRange(area, 1700, 2200);
        Assert.True(area < (quad.Points.Max(p => p.X) - quad.Points.Min(p => p.X)) *
            (quad.Points.Max(p => p.Y) - quad.Points.Min(p => p.Y)) * .8);
    }

    [Fact]
    public void DbTraversesHoleContoursInsteadOfExternalContoursOnly()
    {
        var map = Map(80, 80, (x, y) =>
            x >= 5 && x <= 73 && y >= 5 && y <= 73 &&
            !(x >= 32 && x <= 35 && y >= 32 && y <= 35) ? .9f :
            x >= 32 && x <= 35 && y >= 32 && y <= 35 ? .3f : 0);
        var regions = Decode(map);
        // The hole stays below the bitmap threshold; its foreground rim raises its rectangle mean over .5.
        Assert.Equal(2, regions.Count);
        Assert.Contains(regions, q => q.Score > .8);
        Assert.Contains(regions, q => q.Score >= .5 && q.Score < .7);
    }

    [Fact]
    public void DbFindsNonDiagonalMinimumAreaRectangleRatherThanBoundingAxisBox()
    {
        double radians = 27 * Math.PI / 180;
        double cosine = Math.Cos(radians), sine = Math.Sin(radians);
        var map = Map(256, 128, (x, y) =>
        {
            double u = (x - 128) * cosine + (y - 64) * sine;
            double v = -(x - 128) * sine + (y - 64) * cosine;
            return Math.Abs(u) <= 45 && Math.Abs(v) <= 7 ? 1 : 0;
        });
        var quad = Assert.Single(Decode(map));
        double dx = quad.Points[1].X - quad.Points[0].X;
        double dy = quad.Points[1].Y - quad.Points[0].Y;
        Assert.InRange(Math.Abs(Math.Atan2(dy, dx) - radians), 0, .02);
        Assert.InRange(quad.Score, .7f, 1);
        Assert.InRange(quad.Points.Average(p => p.X), 128.4, 128.6);
        Assert.InRange(quad.Points.Average(p => p.Y), 64.4, 64.6);
    }

    [Fact]
    public void DbContoursTouchingDiagonallyRemainConnectedAndDeterministic()
    {
        var map = Map(64, 64, (x, y) =>
            (x >= 10 && x <= 18 && y >= 10 && y <= 18) ||
            (x >= 20 && x <= 28 && y >= 20 && y <= 28) ? 1 : .3f);
        var first = Decode(map);
        var second = Decode(map);
        Assert.Single(first);
        Assert.Equal(first[0].Points, second[0].Points);
        Assert.Equal(first[0].Score, second[0].Score);
    }

    [Fact]
    public void RectifyKeepsOriginalRasterPixelsHomographyAndDetachedOrderedCorners()
    {
        var raster = Raster(12, 12, (x, y) => (x * 10, y * 10, 7, 255));
        RasterPoint[] points = [new(8, 7), new(2, 7), new(8, 3), new(2, 3)];
        var crop = OcrImageProcessing.Rectify(raster, new(points, .8f), default);
        Assert.Equal(6, crop.Width);
        Assert.Equal(4, crop.Height);
        Assert.Equal(new[] { new RasterPoint(2, 3), new(8, 3), new(8, 7), new(2, 7) }, crop.SourceQuad);
        Assert.Equal(new double[] { 1, 0, -2, 0, 1, -3, 0, 0, 1 }, crop.RasterToCrop.Select(v => Math.Round(v, 8)));
        Assert.Equal(new byte[] { 20, 30, 7 }, crop.Bgr.Take(3));
        Assert.Equal(new byte[] { 70, 60, 7 }, crop.Bgr.TakeLast(3));
        points[0] = new(0, 0);
        Assert.Equal(new RasterPoint(8, 7), crop.SourceQuad[2]);
        AssertReadOnly(crop.SourceQuad, new RasterPoint(0, 0));
        AssertReadOnly(crop.RasterToCrop, 0d);
    }

    [Fact]
    public void RectifyPerspectiveMapsAllFourSourceCornersAndSamplesOriginalNotDetectorRaster()
    {
        var raster = Raster(32, 24, (x, y) => (x * 5, y * 7, 100, 255));
        RasterPoint[] points = [new(4, 3), new(25, 5), new(28, 20), new(2, 18)];
        var crop = OcrImageProcessing.Rectify(raster, new(points, 1), default);
        Assert.Equal(26, crop.Width);
        Assert.Equal(15, crop.Height);
        RasterPoint[] expected = [new(0, 0), new(26, 0), new(26, 15), new(0, 15)];
        for (int i = 0; i < 4; i++)
        {
            Near(expected[i], Transform(crop.RasterToCrop, points[i]), 1e-8);
        }

        Assert.Equal(new byte[] { 20, 21, 100 }, crop.Bgr.Take(3));
        Assert.True(Math.Abs(crop.RasterToCrop[6]) + Math.Abs(crop.RasterToCrop[7]) > .0001);
        Assert.Contains(crop.Bgr, p => p > 100);
    }

    [Fact]
    public void RectifyUsesCubicMinusPoint75KernelAndReplicatedBorders()
    {
        var raster = Raster(4, 4, (x, _) => (x * 50, 20, 255, 255));
        var crop = OcrImageProcessing.Rectify(raster,
            Quad(new(-.5, 0), new(3.5, 0), new(3.5, 4), new(-.5, 4)), default);
        Assert.Equal(4, crop.Width);
        Assert.Equal(4, crop.Height);
        // At .5 the cubic weights are [-.09375,.59375,.59375,-.09375].
        Assert.Equal(0, crop.Bgr[0]); // -.5: replicated edge, negative overshoot saturates to zero.
        Assert.Equal(20, crop.Bgr[3]); // .5: -0*.09375 + 0*.59375 + 50*.59375 - 100*.09375 = 20.3125.
        Assert.Equal(75, crop.Bgr[6]); // 1.5: linear ramp in interior.
        Assert.Equal(130, crop.Bgr[9]); // 2.5: replicated right edge yields 129.6875.
        Assert.Equal(crop.Bgr.Take(12), crop.Bgr.TakeLast(12));
        Assert.Equal(20, crop.Bgr[1]);
        Assert.Equal(255, crop.Bgr[2]);
    }

    [Fact]
    public void RectifyDoesNotAutomaticallyRotateTallCropsAndResolvesDiamondTies()
    {
        var raster = Raster(32, 32, (_, _) => (40, 60, 80, 255));
        var tall = OcrImageProcessing.Rectify(raster,
            Quad(new(2, 2), new(6, 2), new(6, 22), new(2, 22)), default);
        Assert.Equal(4, tall.Width);
        Assert.Equal(20, tall.Height);
        var diamond = OcrImageProcessing.Rectify(raster,
            Quad(new(10, 20), new(20, 10), new(10, 0), new(0, 10)), default);
        Assert.Equal(new[] { new RasterPoint(10, 0), new(20, 10), new(10, 20), new(0, 10) }, diamond.SourceQuad);
        Assert.Equal(14, diamond.Width);
        Assert.Equal(14, diamond.Height);
    }

    [Fact]
    public void RectifyRoundsMaximumOpposingEdgeLengthsTiesEven()
    {
        var crop = OcrImageProcessing.Rectify(Raster(16, 16, (_, _) => (0, 0, 0, 255)),
            Quad(new(1, 1), new(5.5, 1), new(5.5, 7.5), new(1, 7.5)), default);
        Assert.Equal(4, crop.Width);
        Assert.Equal(6, crop.Height);
    }

    [Fact]
    public void ReturnedDbListsCannotBeMutated()
    {
        var results = Decode(Map(32, 32, (_, _) => 1));
        AssertReadOnly(results, Quad(new(0, 0), new(1, 0), new(1, 1), new(0, 1)));
        AssertReadOnly(results[0].Points, new RasterPoint(10, 10));
    }

    [Fact]
    public void RejectsMalformedDetectorShapeDimensionsAndProbabilityValues()
    {
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.DecodeDetector(
            new DenseTensor<float>(new[] { 1, 32, 32 }), 32, 32, 32, 32, default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.DecodeDetector(
            new DenseTensor<float>(new[] { 2, 1, 32, 32 }), 32, 32, 32, 32, default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.DecodeDetector(
            new DenseTensor<float>(new[] { 1, 2, 32, 32 }), 32, 32, 32, 32, default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.DecodeDetector(
            Map(32, 32, (_, _) => 0), 32, 32, 64, 32, default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.DecodeDetector(
            Map(32, 32, (_, _) => 0), 0, 32, 32, 32, default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.DecodeDetector(
            Map(32, 32, (_, _) => 0), 32, 32, 1024, 32, default));
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.01f, 1.01f })
        {
            var map = Map(32, 32, (_, _) => 0);
            map[0, 0, 31, 31] = invalid;
            var exception = Assert.Throws<InvalidDataException>(() => Decode(map));
            Assert.Contains("(31,31)", exception.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DbAcceptsOnlyProvenOneUlpUpperBoundaryNoiseWithoutMutatingRawTensor(bool suppliedAudit)
    {
        var map = Map(32, 32, (_, _) => 1);
        float boundaryNoise = MathF.BitIncrement(1f);
        map[0, 0, 9, 7] = boundaryNoise;
        int[] originalBits = map.Select(BitConverter.SingleToInt32Bits).ToArray();
        var audit = suppliedAudit ? new OcrProbabilityAudit() : null;

        var region = Assert.Single(OcrImageProcessing.DecodeDetector(map, 32, 32, 32, 32, default, audit));

        Assert.Equal(1, region.Score);
        Assert.Equal(originalBits, map.Select(BitConverter.SingleToInt32Bits));
        Assert.Equal(0x3F800001, BitConverter.SingleToInt32Bits(map[0, 0, 9, 7]));
        Assert.Equal(new RasterPoint(0, 0), region.Points[0]);
        Assert.Equal(new RasterPoint(31, 31), region.Points[2]);
        if (audit is not null)
        {
            Assert.Equal(1, audit.AdjustmentCount);
            Assert.Equal(9 * 32 + 7, audit.FirstAdjustedIndex);
            Assert.Equal(boundaryNoise, audit.FirstRawValue);
            Assert.Equal(1, audit.MinimumRawValue);
            Assert.Equal(boundaryNoise, audit.MaximumRawValue);
        }
    }

    [Fact]
    public void DbNormalizesAllValidatedOneUlpValuesForScoringWithoutChangingRawOutput()
    {
        float boundaryNoise = MathF.BitIncrement(1f);
        var map = Map(32, 32, (_, _) => boundaryNoise);
        var audit = new OcrProbabilityAudit();

        var region = Assert.Single(OcrImageProcessing.DecodeDetector(map, 32, 32, 32, 32, default, audit));

        Assert.Equal(1, region.Score);
        Assert.All(map, value => Assert.Equal(0x3F800001, BitConverter.SingleToInt32Bits(value)));
        Assert.Equal(32 * 32, audit.AdjustmentCount);
        Assert.Equal(0, audit.FirstAdjustedIndex);
        Assert.Equal(boundaryNoise, audit.MinimumRawValue);
        Assert.Equal(boundaryNoise, audit.MaximumRawValue);
    }

    public static IEnumerable<object[]> InvalidDbBoundaryValues()
    {
        yield return [MathF.BitIncrement(MathF.BitIncrement(1f))];
        yield return [-float.Epsilon];
        yield return [-1f];
        yield return [float.MinValue];
        yield return [float.NaN];
        yield return [float.PositiveInfinity];
        yield return [float.NegativeInfinity];
    }

    [Theory]
    [MemberData(nameof(InvalidDbBoundaryValues))]
    public void DbRejectsBeyondOneUlpAllNegativeAndNonfiniteProbabilitiesWithoutMutatingOutput(float invalid)
    {
        var map = Map(32, 32, (_, _) => MathF.BitIncrement(1f));
        map[0, 0, 31, 31] = invalid;
        int[] originalBits = map.Select(BitConverter.SingleToInt32Bits).ToArray();
        var audit = new OcrProbabilityAudit();

        var error = Assert.Throws<InvalidDataException>(() =>
            OcrImageProcessing.DecodeDetector(map, 32, 32, 32, 32, default, audit));

        Assert.Contains("(31,31)", error.Message);
        Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Equal(originalBits, map.Select(BitConverter.SingleToInt32Bits));
    }

    [Fact]
    public void RejectsInvalidBuffersDegenerateQuadsAndBudgetsBeforeImageAllocation()
    {
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.PrepareDetector(new([], 0, 2, 1, 1), default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.PrepareDetector(new([], 2, 2, 1, 1), default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.PrepareDetector(new([], int.MaxValue, 2, 1, 1), default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.PrepareRecognizer(Crop(2, 2, []), default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.PrepareRecognizer(Crop(32_000_001, 1, []), default));
        var widthError = Assert.Throws<InvalidDataException>(() =>
            OcrImageProcessing.PrepareRecognizer(Crop(4097, 48, []), default));
        Assert.Contains("4096", widthError.Message);

        var raster = Raster(4, 4, (_, _) => (0, 0, 0, 255));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.Rectify(raster, new([], 1), default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.Rectify(raster,
            Quad(new(0, 0), new(1, 0), new(2, 0), new(3, 0)), default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.Rectify(raster,
            Quad(new(0, 0), new(1, 0), new(1, 1), new(double.NaN, 1)), default));
        Assert.Throws<InvalidDataException>(() => OcrImageProcessing.Rectify(raster,
            Quad(new(0, 0), new(4, 0), new(1, 1), new(0, 4)), default));
        var cropError = Assert.Throws<InvalidDataException>(() => OcrImageProcessing.Rectify(raster,
            Quad(new(0, 0), new(8000, 0), new(8000, 8000), new(0, 8000)), default));
        Assert.Contains("pixel", cropError.Message);
        var narrowError = Assert.Throws<InvalidDataException>(() => OcrImageProcessing.Rectify(raster,
            Quad(new(0, 0), new(4097, 0), new(4097, 48), new(0, 48)), default));
        Assert.Contains("4096", narrowError.Message);
    }

    [Fact]
    public void DbContourCapIsExplicitUnsupportedInputNotSilentTruncation()
    {
        var map = Map(256, 256, (x, y) => x % 6 == 1 && y % 6 == 1 ? 1 : 0);
        var error = Assert.Throws<InvalidDataException>(() => Decode(map));
        Assert.Contains("contour budget", error.Message);
    }

    [Fact]
    public void DbBoundaryBudgetPrecedesUnboundedTraversal()
    {
        var map = Map(960, 960, (x, y) => x % 3 == 0 && y % 3 == 0 ? 1 : 0);
        var error = Assert.Throws<InvalidDataException>(() => Decode(map));
        Assert.Contains("boundary edge budget", error.Message);
    }

    [Fact]
    public void DbSingleComplexContourHasPointBudgetBeforeHullSorting()
    {
        var map = Map(960, 960, (x, y) =>
            x >= 10 && x <= 950 && y >= 10 && y <= 950 && (y <= 12 || x % 10 <= 2) ? 1 : 0);
        var error = Assert.Throws<InvalidDataException>(() => Decode(map));
        Assert.Contains("contour point budget", error.Message);
    }

    [Fact]
    public void AllEntryPointsHonorPreCanceledTokenBeforeValidation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => OcrImageProcessing.PrepareDetector(new([], 0, 0, 0, 0), cts.Token));
        Assert.Throws<OperationCanceledException>(() => OcrImageProcessing.DecodeDetector(
            new DenseTensor<float>(new[] { 1 }), 0, 0, 0, 0, cts.Token));
        Assert.Throws<OperationCanceledException>(() => OcrImageProcessing.Rectify(
            new([], 0, 0, 0, 0), new([], 0), cts.Token));
        Assert.Throws<OperationCanceledException>(() => OcrImageProcessing.PrepareRecognizer(Crop(0, 0, []), cts.Token));
    }

    [Theory]
    [InlineData(37, 64)]
    [InlineData(1025, 1056)]
    public void DbChecksCancellationDuringProbabilityScanAndOriginalRectangleScore(int cancelAtRead, int expectedReads)
    {
        using var cts = new CancellationTokenSource();
        var map = new CancelingTensor(cts, cancelAtRead);
        Assert.Throws<OperationCanceledException>(() =>
            OcrImageProcessing.DecodeDetector(map, 32, 32, 32, 32, cts.Token));
        Assert.Equal(expectedReads, map.Reads);
    }

    private sealed class CancelingTensor(CancellationTokenSource cancellation, int cancelAtRead)
        : DenseTensor<float>(new[] { 1, 1, 32, 32 })
    {
        internal int Reads { get; private set; }

        public override float GetValue(int index)
        {
            if (++Reads == cancelAtRead)
            {
                cancellation.Cancel();
            }

            return 1;
        }
    }

    private static IReadOnlyList<OcrQuad> Decode(DenseTensor<float> map) =>
        OcrImageProcessing.DecodeDetector(map, map.Dimensions[3], map.Dimensions[2],
            map.Dimensions[3], map.Dimensions[2], default);

    private static DenseTensor<float> Map(int width, int height, Func<int, int, float> value)
    {
        var map = new DenseTensor<float>(new[] { 1, 1, height, width });
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                map[0, 0, y, x] = value(x, y);
            }
        }

        return map;
    }

    private static PageRaster Raster(int width, int height, Func<int, int, (int B, int G, int R, int A)> pixel)
    {
        var bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (b, g, r, a) = pixel(x, y);
                int i = (y * width + x) * 4;
                bytes[i] = (byte)b;
                bytes[i + 1] = (byte)g;
                bytes[i + 2] = (byte)r;
                bytes[i + 3] = (byte)a;
            }
        }

        return new(bytes, width, height, width, height);
    }

    private static RectifiedCrop Crop(int width, int height, byte[] bytes) => new(bytes, width, height, [], []);

    private static OcrQuad Quad(params RasterPoint[] points) => new(points, 1);

    private static float Normalize(byte value) => (value / 255f - .5f) / .5f;

    private static void Near(RasterPoint expected, RasterPoint actual, double tolerance)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
    }

    private static RasterPoint Transform(IReadOnlyList<double> h, RasterPoint p)
    {
        double w = h[6] * p.X + h[7] * p.Y + h[8];
        return new((h[0] * p.X + h[1] * p.Y + h[2]) / w, (h[3] * p.X + h[4] * p.Y + h[5]) / w);
    }

    private static double Area(IReadOnlyList<RasterPoint> points) =>
        Math.Abs(Enumerable.Range(0, points.Count).Sum(i =>
            points[i].X * points[(i + 1) % points.Count].Y -
            points[(i + 1) % points.Count].X * points[i].Y)) / 2;

    private static void AssertReadOnly<T>(IReadOnlyList<T> list, T replacement)
    {
        var mutableInterface = Assert.IsAssignableFrom<IList<T>>(list);
        Assert.True(mutableInterface.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableInterface[0] = replacement);
    }
}
