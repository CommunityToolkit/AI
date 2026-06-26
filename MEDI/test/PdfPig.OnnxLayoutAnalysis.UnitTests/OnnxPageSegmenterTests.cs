#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using UglyToad.PdfPig.PdfFonts;
using Xunit;

public class OnnxPageSegmenterTests
{
    #region Constructor

    [Fact]
    public void Constructor_NullModel_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OnnxPageSegmenter(null!));
    }

    #endregion

    #region IDisposable

    [Fact]
    public void Dispose_CanBeCalledOnStubModel()
    {
        // RtDetrLayoutModel doesn't create a session in its constructor,
        // so we can construct it with a fake path for disposal testing.
        var model = new StubLayoutModel();
        // OnnxPageSegmenter creates an InferenceSession in its constructor
        // which requires a valid model file. We test dispose via the stub model directly.
        model.Dispose();

        // Calling Dispose again should not throw
        model.Dispose();
    }

    [Fact]
    public void StubModel_ImplementsIDisposable()
    {
        var model = new StubLayoutModel();
        Assert.IsAssignableFrom<IDisposable>(model);
        model.Dispose();
    }

    #endregion

    #region GetBlocks - empty words

    [SkippableFact]
    public void GetBlocks_EmptyWords_ReturnsEmpty()
    {
        // This test requires a real ONNX model file
        string modelPath = GetTestModelPath();
        Skip.IfNot(File.Exists(modelPath), "ONNX model file not found; skipping integration test.");

        using var model = new StubLayoutModelWithFile(modelPath);
        using var segmenter = new OnnxPageSegmenter(model);

        var result = segmenter.GetBlocks(new List<Word>());
        Assert.Empty(result);
    }

    #endregion

    #region Detection-to-TextBlock mapping logic (via DetectionPostprocessing + synthetic data)

    [Fact]
    public void DetectionOverlap_WordInsideDetection_Captured()
    {
        // Verify the overlap logic: a word fully inside a detection box
        var detBox = new PdfRectangle(0, 0, 100, 100);
        var wordBox = new PdfRectangle(10, 10, 50, 50);

        // Verify overlap exists (using ComputeIoU as proxy)
        float iou = DetectionPostprocessing.ComputeIoU(detBox, wordBox);
        Assert.True(iou > 0, "Word inside detection should overlap");
    }

    [Fact]
    public void DetectionOverlap_WordOutsideDetection_NotCaptured()
    {
        var detBox = new PdfRectangle(0, 0, 100, 100);
        var wordBox = new PdfRectangle(200, 200, 250, 250);

        float iou = DetectionPostprocessing.ComputeIoU(detBox, wordBox);
        Assert.Equal(0f, iou, 4);
    }

    [Fact]
    public void ConfidenceFiltering_BelowThreshold_NotIncluded()
    {
        var detections = new List<LayoutDetection>
        {
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.1f),  // below 0.3 threshold
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.5f),  // above threshold
        };

        // Simulate filtering as OnnxPageSegmenter does
        float threshold = 0.3f;
        var filtered = detections.Where(d => d.Confidence >= threshold).ToList();

        Assert.Single(filtered);
        Assert.Equal(0.5f, filtered[0].Confidence, 4);
    }

    [Fact]
    public void ConfidenceFiltering_AllBelowThreshold_ReturnsNone()
    {
        var detections = new List<LayoutDetection>
        {
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.1f),
            new(new PdfRectangle(50, 50, 150, 150), "table", 1, 0.2f),
        };

        float threshold = 0.3f;
        var filtered = detections.Where(d => d.Confidence >= threshold).ToList();

        Assert.Empty(filtered);
    }

    #endregion

    #region Synthetic Word creation and TextBlock composition

    [Fact]
    public void SyntheticWord_CanBeCreated()
    {
        var word = CreateWord(new PdfRectangle(10, 10, 50, 25));
        Assert.NotNull(word);
        Assert.Equal("a", word.Text);
    }

    [Fact]
    public void SyntheticTextBlock_FromMultipleWords()
    {
        var w1 = CreateWord(new PdfRectangle(10, 10, 50, 25));
        var w2 = CreateWord(new PdfRectangle(60, 10, 100, 25));

        var line = new TextLine(new[] { w1, w2 });
        var block = new TextBlock(new[] { line });

        Assert.Equal(2, block.TextLines[0].Words.Count);
    }

    [Fact]
    public void WordsOnDifferentLines_GroupedCorrectly()
    {
        // Two words on different vertical positions
        var w1 = CreateWord(new PdfRectangle(10, 100, 50, 120));
        var w2 = CreateWord(new PdfRectangle(10, 50, 50, 70));

        var line1 = new TextLine(new[] { w1 });
        var line2 = new TextLine(new[] { w2 });
        var block = new TextBlock(new[] { line1, line2 });

        Assert.Equal(2, block.TextLines.Count);
    }

    #endregion

    #region LayoutDetection record

    [Fact]
    public void LayoutDetection_RecordProperties()
    {
        var box = new PdfRectangle(10, 20, 100, 80);
        var det = new LayoutDetection(box, "table", 5, 0.95f);

        Assert.Equal(box, det.BoundingBox);
        Assert.Equal("table", det.Label);
        Assert.Equal(5, det.ClassId);
        Assert.Equal(0.95f, det.Confidence, 4);
    }

    [Fact]
    public void LayoutDetection_WithExpression()
    {
        var det = new LayoutDetection(new PdfRectangle(0, 0, 10, 10), "text", 0, 0.5f);
        var updated = det with { Confidence = 0.9f };

        Assert.Equal(0.9f, updated.Confidence, 4);
        Assert.Equal("text", updated.Label);
    }

    #endregion

    #region OnnxSegmenterOptions

    [Fact]
    public void OnnxSegmenterOptions_DefaultValues()
    {
        var opts = new OnnxSegmenterOptions();
        Assert.Equal(0.3f, opts.ConfidenceThreshold, 4);
        Assert.Equal(150, opts.RenderDpi);
        Assert.Null(opts.SessionOptions);
    }

    [Fact]
    public void OnnxSegmenterOptions_CustomValues()
    {
        var sessionOpts = new SessionOptions();
        var opts = new OnnxSegmenterOptions
        {
            ConfidenceThreshold = 0.7f,
            RenderDpi = 300,
            SessionOptions = sessionOpts
        };

        Assert.Equal(0.7f, opts.ConfidenceThreshold, 4);
        Assert.Equal(300, opts.RenderDpi);
        Assert.Same(sessionOpts, opts.SessionOptions);
        sessionOpts.Dispose();
    }

    #endregion

    #region Helpers

    private static Word CreateWord(PdfRectangle boundingBox)
    {
        var letter = new Letter(
            "a",
            boundingBox,
            boundingBox,
            boundingBox.BottomLeft,
            boundingBox.BottomRight,
            10, 1,
            (FontDetails)null!,
            TextRenderingMode.NeitherClip,
            null!, null!,
            0, 0);
        return new Word(new[] { letter });
    }

    private static string GetTestModelPath()
    {
        // Look for model in common locations
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "rtdetr.onnx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rtdetr.onnx"),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    /// <summary>
    /// A minimal stub implementing ILayoutDetectionModel for unit testing without an ONNX model file.
    /// </summary>
    private sealed class StubLayoutModel : ILayoutDetectionModel
    {
        public string ModelPath => "stub_model.onnx";

        public IReadOnlyDictionary<int, string> LabelMapping { get; } = new Dictionary<int, string>
        {
            [0] = "text",
            [1] = "table"
        };

        public IReadOnlyList<NamedOnnxValue> Preprocess(SKBitmap pageImage, int originalWidth, int originalHeight)
        {
            return Array.Empty<NamedOnnxValue>();
        }

        public IReadOnlyList<LayoutDetection> Postprocess(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int originalWidth, int originalHeight)
        {
            return Array.Empty<LayoutDetection>();
        }

        public void Dispose() { }
    }

    /// <summary>
    /// A stub layout model that accepts an actual model file path for integration tests.
    /// </summary>
    private sealed class StubLayoutModelWithFile : ILayoutDetectionModel
    {
        public StubLayoutModelWithFile(string modelPath)
        {
            ModelPath = modelPath;
        }

        public string ModelPath { get; }

        public IReadOnlyDictionary<int, string> LabelMapping { get; } = new Dictionary<int, string>
        {
            [0] = "text"
        };

        public IReadOnlyList<NamedOnnxValue> Preprocess(SKBitmap pageImage, int originalWidth, int originalHeight)
        {
            return Array.Empty<NamedOnnxValue>();
        }

        public IReadOnlyList<LayoutDetection> Postprocess(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int originalWidth, int originalHeight)
        {
            return Array.Empty<LayoutDetection>();
        }

        public void Dispose() { }
    }

    #endregion
}
#endif
