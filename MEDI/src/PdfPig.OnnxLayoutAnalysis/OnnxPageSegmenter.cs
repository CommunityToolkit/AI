using Microsoft.Extensions.Options;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// Page segmenter that uses an ONNX layout detection model to identify
/// document regions and assign words to detected blocks.
/// </summary>
public class OnnxPageSegmenter : IPageSegmenter, IDisposable, IAsyncDisposable
{
    private readonly ILayoutDetectionModel _model;
    private readonly Microsoft.ML.OnnxRuntime.InferenceSession _session;
    private readonly float _confidenceThreshold;
    private readonly int _renderDpi;
    private bool _disposed;

    /// <summary>
    /// Create a new <see cref="OnnxPageSegmenter"/> using dependency-injected options.
    /// </summary>
    /// <param name="model">The layout detection model to use.</param>
    /// <param name="options">The configured options.</param>
    public OnnxPageSegmenter(ILayoutDetectionModel model, IOptions<OnnxSegmenterOptions> options)
        : this(model, GetOptionsValue(options))
    {
    }

    /// <summary>
    /// Create a new <see cref="OnnxPageSegmenter"/>.
    /// </summary>
    /// <param name="model">The layout detection model to use.</param>
    /// <param name="options">Optional segmenter configuration.</param>
    public OnnxPageSegmenter(ILayoutDetectionModel model, OnnxSegmenterOptions? options = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _confidenceThreshold = options?.ConfidenceThreshold ?? 0.3f;
        _renderDpi = options?.RenderDpi ?? 150;
        var sessionOpts = options?.SessionOptions ?? new Microsoft.ML.OnnxRuntime.SessionOptions();
        _session = new Microsoft.ML.OnnxRuntime.InferenceSession(model.ModelPath, sessionOpts);
    }

    /// <summary>
    /// Get text blocks by running ONNX layout detection and assigning words to detected regions.
    /// </summary>
    /// <param name="words">The page's words to generate text blocks for.</param>
    /// <returns>A list of text blocks from this approach.</returns>
    public IReadOnlyList<TextBlock> GetBlocks(IEnumerable<Word> words)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wordList = words?.ToList() ?? throw new ArgumentNullException(nameof(words));
        if (wordList.Count == 0)
        {
            return Array.Empty<TextBlock>();
        }

        // 1. Compute page bounds from words
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var word in wordList)
        {
            var bb = word.BoundingBox;
            minX = Math.Min(minX, bb.Left);
            minY = Math.Min(minY, bb.Bottom);
            maxX = Math.Max(maxX, bb.Right);
            maxY = Math.Max(maxY, bb.Top);
        }

        double pageWidth = maxX - minX;
        double pageHeight = maxY - minY;

        if (pageWidth <= 0 || pageHeight <= 0)
        {
            return [new TextBlock(CreateSingleLine(wordList))];
        }

        // 2. Render page image using PageImageRenderer
        using SKBitmap pageImage = PageImageRenderer.RenderWords(wordList, pageWidth, pageHeight, _renderDpi);
        int imageWidth = pageImage.Width;
        int imageHeight = pageImage.Height;

        // 3. Preprocess with model
        var inputs = _model.Preprocess(pageImage, imageWidth, imageHeight);

        // 4. Run inference
        using var results = _session.Run(inputs);

        // 5. Postprocess with model
        var detections = _model.Postprocess(results, imageWidth, imageHeight);

        // 6. Filter by confidence
        var filtered = new List<LayoutDetection>();
        foreach (var det in detections)
        {
            if (det.Confidence >= _confidenceThreshold)
            {
                filtered.Add(det);
            }
        }

        if (filtered.Count == 0)
        {
            return [new TextBlock(CreateSingleLine(wordList))];
        }

        // 7. Map detections to TextBlocks via bbox overlap
        return MapDetectionsToBlocks(filtered, wordList, pageWidth, pageHeight, minX, minY);
    }

    private static List<TextBlock> MapDetectionsToBlocks(
        List<LayoutDetection> detections,
        List<Word> words,
        double pageWidth,
        double pageHeight,
        double offsetX,
        double offsetY)
    {
        var wordAssigned = new bool[words.Count];
        var blocks = new List<TextBlock>();

        foreach (var detection in detections)
        {
            var capturedWords = new List<Word>();

            for (int i = 0; i < words.Count; i++)
            {
                if (wordAssigned[i])
                {
                    continue;
                }

                if (HasOverlap(detection.BoundingBox, words[i].BoundingBox))
                {
                    capturedWords.Add(words[i]);
                    wordAssigned[i] = true;
                }
            }

            if (capturedWords.Count > 0)
            {
                var lines = GroupWordsIntoLines(capturedWords);
                blocks.Add(new AnnotatedTextBlock(lines, detection.Label, detection.Confidence));
            }
        }

        // 8. Handle uncaptured words — group into a fallback block
        var uncaptured = new List<Word>();
        for (int i = 0; i < words.Count; i++)
        {
            if (!wordAssigned[i])
            {
                uncaptured.Add(words[i]);
            }
        }

        if (uncaptured.Count > 0)
        {
            var lines = GroupWordsIntoLines(uncaptured);
            blocks.Add(new TextBlock(lines));
        }

        return blocks;
    }

    private static bool HasOverlap(PdfRectangle a, PdfRectangle b)
    {
        double interLeft = Math.Max(a.Left, b.Left);
        double interRight = Math.Min(a.Right, b.Right);
        double interBottom = Math.Max(a.Bottom, b.Bottom);
        double interTop = Math.Min(a.Top, b.Top);

        return interLeft < interRight && interBottom < interTop;
    }

    private static IReadOnlyList<TextLine> GroupWordsIntoLines(List<Word> words)
    {
        if (words.Count == 0)
        {
            return Array.Empty<TextLine>();
        }

        // Sort words by vertical position (top to bottom), then left to right
        words.Sort((a, b) =>
        {
            double aY = a.BoundingBox.Top;
            double bY = b.BoundingBox.Top;

            // Group by Y-proximity: if vertical centers are close, treat as same line
            double aCenter = (a.BoundingBox.Top + a.BoundingBox.Bottom) / 2.0;
            double bCenter = (b.BoundingBox.Top + b.BoundingBox.Bottom) / 2.0;
            double aHeight = a.BoundingBox.Height;
            double bHeight = b.BoundingBox.Height;
            double tolerance = Math.Min(aHeight, bHeight) * 0.5;

            if (Math.Abs(aCenter - bCenter) <= tolerance)
            {
                return a.BoundingBox.Left.CompareTo(b.BoundingBox.Left);
            }

            // Higher Y = higher on page in PDF coords, so sort descending
            return bY.CompareTo(aY);
        });

        var lines = new List<TextLine>();
        var currentLineWords = new List<Word> { words[0] };

        for (int i = 1; i < words.Count; i++)
        {
            var prev = currentLineWords[^1];
            var curr = words[i];

            double prevCenter = (prev.BoundingBox.Top + prev.BoundingBox.Bottom) / 2.0;
            double currCenter = (curr.BoundingBox.Top + curr.BoundingBox.Bottom) / 2.0;
            double tolerance = Math.Min(prev.BoundingBox.Height, curr.BoundingBox.Height) * 0.5;

            if (Math.Abs(prevCenter - currCenter) <= tolerance)
            {
                currentLineWords.Add(curr);
            }
            else
            {
                // Sort current line words left to right before creating line
                currentLineWords.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
                lines.Add(new TextLine(currentLineWords.ToList()));
                currentLineWords.Clear();
                currentLineWords.Add(curr);
            }
        }

        if (currentLineWords.Count > 0)
        {
            currentLineWords.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            lines.Add(new TextLine(currentLineWords.ToList()));
        }

        return lines;
    }

    private static IReadOnlyList<TextLine> CreateSingleLine(List<Word> words)
    {
        return [new TextLine(words)];
    }

    private static OnnxSegmenterOptions GetOptionsValue(IOptions<OnnxSegmenterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Value;
    }

    /// <summary>
    /// Dispose resources held by this segmenter.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously dispose resources held by this segmenter.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Dispose managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _session.Dispose();
                _model.Dispose();
            }

            _disposed = true;
        }
    }
}
