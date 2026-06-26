using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.Models;

/// <summary>
/// Layout detection model implementation for the Docling Layout Heron RT-DETR v2 model.
/// </summary>
/// <remarks>
/// <para>
/// RT-DETR (Real-Time DEtection TRansformer) uses built-in Hungarian matching,
/// so no external NMS is needed.
/// </para>
/// <para>
/// Input: Resize to 640×640 exact, uint8 CHW tensor (normalization is baked into the ONNX graph).
/// Also provides an <c>orig_target_sizes</c> int64 tensor with [height, width].
/// </para>
/// <para>
/// Output: Handles both <c>labels</c>/<c>boxes</c>/<c>scores</c> and
/// <c>pred_labels</c>/<c>pred_boxes</c>/<c>pred_scores</c> naming conventions.
/// </para>
/// </remarks>
public sealed class RtDetrLayoutModel : ILayoutDetectionModel
{
    private const int ModelInputWidth = 640;
    private const int ModelInputHeight = 640;

    private static readonly Dictionary<int, string> DefaultLabelMapping = new Dictionary<int, string>
    {
        [0] = "caption",
        [1] = "footnote",
        [2] = "formula",
        [3] = "list_item",
        [4] = "page_footer",
        [5] = "page_header",
        [6] = "picture",
        [7] = "section_header",
        [8] = "table",
        [9] = "text",
        [10] = "title",
        [11] = "document_index",
        [12] = "code",
        [13] = "checkbox_selected",
        [14] = "checkbox_unselected",
        [15] = "form",
        [16] = "key_value_region"
    };

    private bool _disposed;

    /// <summary>
    /// Create a new <see cref="RtDetrLayoutModel"/>.
    /// </summary>
    /// <param name="modelPath">Path to the RT-DETR ONNX model file.</param>
    public RtDetrLayoutModel(string modelPath)
    {
        ModelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
    }

    /// <inheritdoc />
    public string ModelPath { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<int, string> LabelMapping => DefaultLabelMapping;

    /// <inheritdoc />
    public IReadOnlyList<NamedOnnxValue> Preprocess(SKBitmap pageImage, int originalWidth, int originalHeight)
    {
        ArgumentNullException.ThrowIfNull(pageImage);

        // Resize to model input dimensions (exact, no letterbox)
        using var resized = ImagePreprocessing.ResizeExact(pageImage, ModelInputWidth, ModelInputHeight);

        // Convert to CHW uint8 tensor — normalization is baked into the ONNX graph
        var imageTensor = ImagePreprocessing.ToChwUint8(resized);

        // Create orig_target_sizes tensor [1, 2] with [height, width]
        var origSizesTensor = new DenseTensor<long>([1, 2]);
        origSizesTensor[0, 0] = originalHeight;
        origSizesTensor[0, 1] = originalWidth;

        return new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", imageTensor),
            NamedOnnxValue.CreateFromTensor("orig_target_sizes", origSizesTensor)
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<LayoutDetection> Postprocess(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int originalWidth,
        int originalHeight)
    {
        ArgumentNullException.ThrowIfNull(results);

        // Try both naming conventions
        var labelsValue = TryGetOutput(results, "labels") ?? TryGetOutput(results, "pred_labels");
        var boxesValue = TryGetOutput(results, "boxes") ?? TryGetOutput(results, "pred_boxes");
        var scoresValue = TryGetOutput(results, "scores") ?? TryGetOutput(results, "pred_scores");

        if (labelsValue is null || boxesValue is null || scoresValue is null)
        {
            return Array.Empty<LayoutDetection>();
        }

        var labels = ExtractLabels(labelsValue);
        var scores = ExtractScores(scoresValue);
        var boxes = ExtractBoxes(boxesValue);

        if (labels is null || scores is null || boxes is null)
        {
            return Array.Empty<LayoutDetection>();
        }

        int count = labels.Length;
        var detections = new List<LayoutDetection>(count);

        for (int i = 0; i < count; i++)
        {
            int classId = labels[i];
            float confidence = scores[i];

            // Boxes are in absolute pixel coordinates (post orig_target_sizes scaling)
            float x1 = boxes[i * 4 + 0];
            float y1 = boxes[i * 4 + 1];
            float x2 = boxes[i * 4 + 2];
            float y2 = boxes[i * 4 + 3];

            string label = DefaultLabelMapping.TryGetValue(classId, out var name)
                ? name
                : $"class_{classId}";

            // Convert from image coords (top-left origin) to PDF coords (bottom-left origin)
            var rect = DetectionPostprocessing.XyxyToRect(x1, y1, x2, y2, originalWidth, originalHeight);

            detections.Add(new LayoutDetection(rect, label, classId, confidence));
        }

        return detections;
    }

    private static DisposableNamedOnnxValue? TryGetOutput(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        string name)
    {
        foreach (var result in results)
        {
            if (string.Equals(result.Name, name, StringComparison.Ordinal))
            {
                return result;
            }
        }

        return null;
    }

    private static int[]? ExtractLabels(DisposableNamedOnnxValue value)
    {
        // Try int64 tensor first (most common)
        if (value.Value is Tensor<long> longTensor)
        {
            var dims = longTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var labels = new int[count];

            for (int i = 0; i < count; i++)
            {
                labels[i] = dims.Length > 1
                    ? (int)longTensor[0, i]
                    : (int)longTensor[i];
            }

            return labels;
        }

        // Try int32 tensor
        if (value.Value is Tensor<int> intTensor)
        {
            var dims = intTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var labels = new int[count];

            for (int i = 0; i < count; i++)
            {
                labels[i] = dims.Length > 1
                    ? intTensor[0, i]
                    : intTensor[i];
            }

            return labels;
        }

        return null;
    }

    private static float[]? ExtractScores(DisposableNamedOnnxValue value)
    {
        if (value.Value is Tensor<float> floatTensor)
        {
            var dims = floatTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var scores = new float[count];

            for (int i = 0; i < count; i++)
            {
                scores[i] = dims.Length > 1
                    ? floatTensor[0, i]
                    : floatTensor[i];
            }

            return scores;
        }

        return null;
    }

    private static float[]? ExtractBoxes(DisposableNamedOnnxValue value)
    {
        if (value.Value is Tensor<float> floatTensor)
        {
            var dims = floatTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var boxes = new float[count * 4];

            for (int i = 0; i < count; i++)
            {
                if (dims.Length == 3)
                {
                    // Shape: [batch, num_detections, 4]
                    boxes[i * 4 + 0] = floatTensor[0, i, 0];
                    boxes[i * 4 + 1] = floatTensor[0, i, 1];
                    boxes[i * 4 + 2] = floatTensor[0, i, 2];
                    boxes[i * 4 + 3] = floatTensor[0, i, 3];
                }
                else if (dims.Length == 2)
                {
                    // Shape: [num_detections, 4]
                    boxes[i * 4 + 0] = floatTensor[i, 0];
                    boxes[i * 4 + 1] = floatTensor[i, 1];
                    boxes[i * 4 + 2] = floatTensor[i, 2];
                    boxes[i * 4 + 3] = floatTensor[i, 3];
                }
            }

            return boxes;
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
