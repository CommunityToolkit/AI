// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class OcrContractTests
{
    [Theory]
    [InlineData(OnnxOcrMode.PagesWithoutNativeText)]
    [InlineData(OnnxOcrMode.WholePage)]
    public void OcrPoliciesSnapshotAllConfiguration(OnnxOcrMode mode)
    {
        using var sessionOptions = new SessionOptions();
        var options = new OnnxLayoutOptions
        {
            Ocr = new() { Mode = mode, DetectorModelPath = "det.onnx", RecognizerModelPath = "rec.onnx" },
            SessionOptions = sessionOptions, UseColumnReadingOrder = true,
        };
        var captured = options.Snapshot();
        options.Ocr.Mode = OnnxOcrMode.WholePage;
        options.Ocr.DetectorModelPath = "other";
        options.Ocr.RecognizerModelPath = null;
        options.SessionOptions = null;
        options.UseColumnReadingOrder = false;
        Assert.Equal(mode, captured.Ocr!.Mode);
        Assert.Equal("det.onnx", captured.Ocr.DetectorModelPath);
        Assert.Equal("rec.onnx", captured.Ocr.RecognizerModelPath);
        Assert.Same(sessionOptions, captured.SessionOptions);
        Assert.True(captured.UseColumnReadingOrder);
        var defaults = new OnnxLayoutOptions().Snapshot();
        Assert.Null(defaults.Ocr);
        Assert.Null(defaults.SessionOptions);
    }

    [Fact]
    public void InvalidPolicyAndMissingEnabledPathsFailBeforeLoading()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxLayoutOptions { Ocr = new() { Mode = (OnnxOcrMode)123 } }.Snapshot());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnnxLayoutOptions { Ocr = new() { Mode = (OnnxOcrMode)0 } }.Snapshot());
        Assert.Throws<ArgumentNullException>(() => new OnnxLayoutOptions { Ocr = new() }.Snapshot());
        Assert.Throws<ArgumentException>(() => new OnnxLayoutOptions
        {
            Ocr = new() { DetectorModelPath = "det", RecognizerModelPath = " " },
        }.Snapshot());
    }

    [Fact]
    public void DisabledOcrDoesNotLoadModelsAndFailureKeepsBorrowedHeronAlive()
    {
        using var session = new InferenceSession(LayoutCoordinateTests.CreateGraph());
        using (var client = new PdfPigOnnxDocumentExtractionClient(session))
        {
            Assert.Same(session, client.GetService(typeof(InferenceSession)));
        }

        HeronModel.Validate(session);
        Assert.Throws<FileNotFoundException>(() => new PdfPigOnnxDocumentExtractionClient(session, new()
        {
            Ocr = new() { Mode = OnnxOcrMode.WholePage,
                DetectorModelPath = "nonexistent-detector", RecognizerModelPath = "nonexistent-recognizer" },
        }));
        HeronModel.Validate(session);
    }

    [Fact]
    public void CuratedArtifactsAndDictionaryRejectWrongIdentity()
    {
        string temporary = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(temporary, [1, 2, 3]);
            var error = Assert.Throws<InvalidDataException>(() => LocalOnnxSession.Verify(temporary, PaddleOcrModel.DetectorHash));
            Assert.Contains(PaddleOcrModel.DetectorHash, error.Message);
        }
        finally
        {
            File.Delete(temporary);
        }

        Assert.Throws<InvalidDataException>(() => PaddleOcrModel.ReadDictionary("A\nB\n"));
        Assert.Throws<InvalidDataException>(() => PaddleOcrModel.ReadDictionary(string.Join("\n", Enumerable.Repeat("x", 18383))));
    }

    [Fact]
    public void CtcCollapsesBeforeRemovingBlankAndPreservesSpaceZeroAndProbabilities()
    {
        string[] dictionary = ["", "A", "B", " ", "0"];
        int[] classes = [1, 1, 0, 1, 3, 3, 0, 4, 4, 2];
        var probabilities = new DenseTensor<float>([1, classes.Length, dictionary.Length]);
        for (int step = 0; step < classes.Length; step++)
        {
            probabilities[0, step, classes[step]] = .6f + step * .01f;
        }

        var actual = PaddleOcrModel.Decode(probabilities, dictionary, default);
        Assert.Equal("AA 0B", actual.Text);
        Assert.Equal((.6f + .63f + .64f + .67f + .69f) / 5, actual.Confidence, .000001f);
        Assert.NotEqual(1, actual.Confidence);
    }

    [Fact]
    public void EmptyCtcIsNoRecognizedTextNotInventedWhitespace()
    {
        var probabilities = new DenseTensor<float>(new[] { 1f, 0f, .9f, .1f }, [1, 2, 2]);
        var actual = PaddleOcrModel.Decode(probabilities, ["", "A"], default);
        Assert.Equal("", actual.Text);
        Assert.Equal(0, actual.Confidence);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-.01f)]
    [InlineData(1.01f)]
    public void InvalidRecognitionProbabilityReportsStepAndClass(float value)
    {
        var probabilities = new DenseTensor<float>(new[] { .1f, value }, [1, 1, 2]);
        var error = Assert.Throws<InvalidDataException>(() => PaddleOcrModel.Decode(probabilities, ["", "A"], default));
        Assert.Contains("step 0, class 1", error.Message);
    }

    [Fact]
    public void RecognitionDimensionsBudgetAndCancellationRejectExplicitly()
    {
        Assert.Throws<InvalidDataException>(() => PaddleOcrModel.Decode(new DenseTensor<float>([2, 1, 2]), ["", "A"], default));
        Assert.Throws<InvalidDataException>(() => PaddleOcrModel.Decode(new DenseTensor<float>([1, 513, 2]), ["", "A"], default));
        Assert.Throws<InvalidDataException>(() => PaddleOcrModel.Decode(new DenseTensor<float>([1, 1, 3]), ["", "A"], default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() =>
            PaddleOcrModel.Decode(new DenseTensor<float>([1, 1, 2]), ["", "A"], cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void ObservationsDetachQuadHomographyAndPreserveDisplayCoordinateFrame()
    {
        var points = new RasterPoint[] { new(20, 10), new(80, 20), new(75, 40), new(15, 30) };
        double[] transform = [1, 0, -20, 0, 1, -10, 0, 0, 1];
        var raster = new PageRaster(new byte[200 * 100 * 4], 200, 100, 400, 200);
        var region = new PdfPigOcrRegion(7, raster, new(points, .8f), new([], 60, 20, transform, points), "0 A", .7f);
        points[0] = new(0, 0);
        transform[0] = 999;
        Assert.Equal(7, region.PageNumber);
        Assert.Equal("0 A", region.Text);
        Assert.Equal(.7f, region.Confidence);
        Assert.Equal(.8f, region.DetectionConfidence);
        Assert.Equal(new PdfPoint(20, 10), region.RasterPolygon[0]);
        Assert.Equal(new PdfPoint(40, 180), region.Polygon[0]);
        Assert.Equal(new PdfPoint(150, 120), region.Polygon[2]);
        Assert.Equal(new PdfRectangle(30, 120, 160, 180), region.Bounds);
        Assert.Equal(1, region.RasterToCropTransform[0]);
        Assert.Equal(60, region.CropWidth);
        Assert.Throws<NotSupportedException>(() => ((IList<PdfPoint>)region.Polygon).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<double>)region.RasterToCropTransform).Clear());
    }

    [Fact]
    public void AuthoritativeOcrReplacesNativeOnceAndRetainsMissesRolesAndLiteralEmptyObservation()
    {
        using var pdf = PdfPageRenderer.Open(LayoutPdfFixture.Create());
        var page = pdf.GetPage(1);
        var document = new ExtractionDocument("ocr");
        HeronDetection[] detections =
        [
            new("title", .9f, new(0, 350, 400, 420)),
            new("table", .9f, new(0, 300, 400, 350)),
            new("picture", .9f, new(0, 430, 200, 530)),
        ];
        var native = LayoutAssembler.AddPage(document, page, detections, default);
        Assert.Contains("LOCAL", string.Join(" ", document.EnumerateContent().Select(e => (e.Content as TextContent)?.Text)));
        PdfPigOcrRegion[] recognized =
        [
            Observation(new(20, 370, 150, 400), "Recognized title"),
            Observation(new(20, 315, 150, 340), "0"),
            Observation(new(20, 200, 150, 220), "missed region"),
            Observation(new(250, 50, 300, 70), ""),
        ];
        var nativeWords = page.GetWords().ToArray();
        var raw = new PdfPigPipelineObservation(native,
            new Dictionary<int, IReadOnlyList<Word>> { [1] = nativeWords }, recognized);
        OcrPageAssembler.ReplaceNativeContent(document.Sections[0], document.SourcePages[0], native, recognized, true, default);
        var all = document.EnumerateContent().ToArray();
        Assert.Equal(new[] { "Recognized title", "0", "missed region" },
            all.Select(e => (e.Content as TextContent)?.Text).Where(s => s is not null));
        Assert.DoesNotContain(all, e => (e.Content as TextContent)?.Text?.Contains("LOCAL", StringComparison.Ordinal) == true);
        Assert.Equal("heading", Assert.IsType<ExtractionDocumentTextElement>(all.Single(e => e.Content is TextContent t && t.Text == "Recognized title")).Role);
        Assert.Single(all.OfType<ExtractionDocumentImageElement>());
        Assert.Equal("0", (Assert.Single(all.OfType<ExtractionDocumentTableElement>()).Content as TextContent)?.Text);
        Assert.Equal(4, raw.OcrRegions.Count);
        Assert.Equal("", raw.OcrRegions[3].Text);
        Assert.Equal(nativeWords, raw.NativePageWords[1]);
        Assert.All(native, region => Assert.Contains(region, raw.NativeRegions));
        Assert.Contains(raw.NativePageWords[1], word => word.Text == "LOCAL");
        foreach (var observation in recognized.Take(3))
        {
            Assert.Single(all.SelectMany(e => e.Sources), s => ReferenceEquals(s.GetRawRepresentation(), observation));
        }

        Assert.Throws<NotSupportedException>(() => ((IList<PdfPigOcrRegion>)raw.OcrRegions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, IReadOnlyList<Word>>)raw.NativePageWords).Clear());
    }

    private static PdfPigOcrRegion Observation(PdfRectangle bounds, string text)
    {
        RasterPoint[] points =
        [
            new(bounds.Left, 550 - bounds.Top), new(bounds.Right, 550 - bounds.Top),
            new(bounds.Right, 550 - bounds.Bottom), new(bounds.Left, 550 - bounds.Bottom),
        ];
        return new(1, new(new byte[400 * 550 * 4], 400, 550, 400, 550), new(points, .9f),
            new([], (int)bounds.Width, (int)bounds.Height, new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, points), text, .8f);
    }
}
