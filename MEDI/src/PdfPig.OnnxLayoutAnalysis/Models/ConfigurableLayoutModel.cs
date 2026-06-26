using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.Models;

/// <summary>
/// A configuration-driven layout detection model implementation.
/// Uses <see cref="LayoutModelOptions"/> to determine preprocessing and postprocessing behavior.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread safety:</b> This type is not thread-safe. The <see cref="Preprocess"/> method
/// stores letterbox state in instance fields that <see cref="Postprocess"/> reads.
/// Concurrent Preprocess/Postprocess call pairs will produce incorrect results.
/// Use a separate instance per thread, or synchronize access externally.
/// </para>
/// </remarks>
public sealed class ConfigurableLayoutModel : ILayoutDetectionModel
{
    private readonly LayoutModelOptions _options;
    private bool _disposed;

    // Letterbox state preserved between pre- and post-processing
    private float _letterboxScale;
    private int _padX;
    private int _padY;
    private bool _wasLetterboxed;

    /// <summary>
    /// Create a new <see cref="ConfigurableLayoutModel"/>.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file.</param>
    /// <param name="options">Model configuration options.</param>
    public ConfigurableLayoutModel(string modelPath, LayoutModelOptions options)
    {
        ModelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string ModelPath { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<int, string> LabelMapping =>
        _options.ClassLabels ?? new Dictionary<int, string>();

    /// <inheritdoc />
    public IReadOnlyList<NamedOnnxValue> Preprocess(SKBitmap pageImage, int originalWidth, int originalHeight)
    {
        ArgumentNullException.ThrowIfNull(pageImage);

        SKBitmap resized;
        _wasLetterboxed = false;

        switch (_options.Resize)
        {
            case ResizeMode.Letterbox:
                resized = ImagePreprocessing.Letterbox(
                    pageImage,
                    _options.InputWidth,
                    _options.InputHeight,
                    SKColors.Gray,
                    out _letterboxScale,
                    out _padX,
                    out _padY);
                _wasLetterboxed = true;
                break;

            case ResizeMode.AspectPreserve:
                float scaleX = (float)_options.InputWidth / pageImage.Width;
                float scaleY = (float)_options.InputHeight / pageImage.Height;
                float scale = Math.Min(scaleX, scaleY);
                int newW = (int)(pageImage.Width * scale);
                int newH = (int)(pageImage.Height * scale);
                resized = ImagePreprocessing.ResizeExact(pageImage, newW, newH);
                break;

            default: // Exact
                resized = ImagePreprocessing.ResizeExact(pageImage, _options.InputWidth, _options.InputHeight);
                break;
        }

        try
        {
            return _options.PixelFormat switch
            {
                PixelFormat.Float32Chw => CreateFloatInput(resized),
                _ => CreateUint8Input(resized)
            };
        }
        finally
        {
            resized.Dispose();
        }
    }

    private static List<NamedOnnxValue> CreateUint8Input(SKBitmap image)
    {
        var tensor = ImagePreprocessing.ToChwUint8(image);
        return new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", tensor)
        };
    }

    private List<NamedOnnxValue> CreateFloatInput(SKBitmap image)
    {
        var tensor = ImagePreprocessing.ToChwFloat(image);

        if (_options.Normalization is not null)
        {
            var buffer = tensor.Buffer.Span;
            ImagePreprocessing.Normalize(
                buffer,
                image.Width,
                image.Height,
                _options.Normalization.Mean.AsSpan(),
                _options.Normalization.Std.AsSpan());
        }

        return new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", tensor)
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<LayoutDetection> Postprocess(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int originalWidth,
        int originalHeight)
    {
        ArgumentNullException.ThrowIfNull(results);

        var detections = ParseOutputTensor(results, originalWidth, originalHeight);

        // Apply confidence filter
        detections = detections
            .Where(d => d.Confidence >= _options.ConfidenceThreshold)
            .ToList();

        // Apply NMS if required
        if (_options.RequiresNms && detections.Count > 1)
        {
            detections = DetectionPostprocessing.ApplyNms(detections, _options.NmsIouThreshold).ToList();
        }

        // Scale to page coordinates if letterboxed
        if (_wasLetterboxed)
        {
            detections = DetectionPostprocessing.ScaleToPage(
                detections,
                _options.InputWidth,
                _options.InputHeight,
                originalWidth,
                originalHeight,
                _letterboxScale,
                _padX,
                _padY).ToList();
        }

        return detections;
    }

    private List<LayoutDetection> ParseOutputTensor(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int originalWidth,
        int originalHeight)
    {
        var detections = new List<LayoutDetection>();

        // Try to find a single output tensor (common YOLO-style: [1, num_classes+4, num_detections])
        DisposableNamedOnnxValue? outputValue = null;
        DisposableNamedOnnxValue? labelsValue = null;
        DisposableNamedOnnxValue? boxesValue = null;
        DisposableNamedOnnxValue? scoresValue = null;

        foreach (var result in results)
        {
            var name = result.Name;
            if (name == "labels" || name == "pred_labels")
            {
                labelsValue = result;
            }
            else if (name == "boxes" || name == "pred_boxes")
            {
                boxesValue = result;
            }
            else if (name == "scores" || name == "pred_scores")
            {
                scoresValue = result;
            }
            else if (name == "output" || name == "output0")
            {
                outputValue = result;
            }
        }

        // If we have separate labels/boxes/scores outputs
        if (labelsValue is not null && boxesValue is not null && scoresValue is not null)
        {
            return ParseSeparateOutputs(labelsValue, boxesValue, scoresValue, originalWidth, originalHeight);
        }

        // Otherwise try a combined output tensor
        if (outputValue?.Value is Tensor<float> combinedTensor)
        {
            return ParseCombinedOutput(combinedTensor, originalWidth, originalHeight);
        }

        // Fallback: try the first float tensor output
        foreach (var result in results)
        {
            if (result.Value is Tensor<float> tensor && tensor.Dimensions.Length >= 2)
            {
                return ParseCombinedOutput(tensor, originalWidth, originalHeight);
            }
        }

        return detections;
    }

    private List<LayoutDetection> ParseSeparateOutputs(
        DisposableNamedOnnxValue labelsValue,
        DisposableNamedOnnxValue boxesValue,
        DisposableNamedOnnxValue scoresValue,
        int originalWidth,
        int originalHeight)
    {
        var detections = new List<LayoutDetection>();

        int[]? labels = ExtractIntArray(labelsValue);
        float[]? scores = ExtractFloatArray(scoresValue);
        float[]? boxes = ExtractBoxArray(boxesValue);

        if (labels is null || scores is null || boxes is null)
        {
            return detections;
        }

        int count = labels.Length;

        for (int i = 0; i < count; i++)
        {
            int classId = labels[i];
            float confidence = scores[i];

            float bx0 = boxes[i * 4 + 0];
            float bx1 = boxes[i * 4 + 1];
            float bx2 = boxes[i * 4 + 2];
            float bx3 = boxes[i * 4 + 3];

            var rect = ConvertBbox(bx0, bx1, bx2, bx3, originalWidth, originalHeight);
            string label = GetLabelName(classId);

            detections.Add(new LayoutDetection(rect, label, classId, confidence));
        }

        return detections;
    }

    private List<LayoutDetection> ParseCombinedOutput(
        Tensor<float> tensor,
        int originalWidth,
        int originalHeight)
    {
        var detections = new List<LayoutDetection>();
        var dims = tensor.Dimensions;

        if (dims.Length == 3)
        {
            // Shape: [1, numDetections, 4+numClasses] or [1, 4+numClasses, numDetections]
            int dim1 = dims[1];
            int dim2 = dims[2];

            bool transposed = dim2 > dim1 && dim1 > 5;

            int numDetections;
            int numValues;

            if (transposed)
            {
                // [1, 4+numClasses, numDetections] — need to transpose
                numDetections = dim2;
                numValues = dim1;
            }
            else
            {
                // [1, numDetections, 4+numClasses]
                numDetections = dim1;
                numValues = dim2;
            }

            int numClasses = numValues - 4;
            if (numClasses <= 0)
            {
                return detections;
            }

            for (int i = 0; i < numDetections; i++)
            {
                float bx0, bx1, bx2, bx3;
                int bestClass = 0;
                float bestScore = float.MinValue;

                if (transposed)
                {
                    bx0 = tensor[0, 0, i];
                    bx1 = tensor[0, 1, i];
                    bx2 = tensor[0, 2, i];
                    bx3 = tensor[0, 3, i];

                    for (int c = 0; c < numClasses; c++)
                    {
                        float score = tensor[0, 4 + c, i];
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestClass = c;
                        }
                    }
                }
                else
                {
                    bx0 = tensor[0, i, 0];
                    bx1 = tensor[0, i, 1];
                    bx2 = tensor[0, i, 2];
                    bx3 = tensor[0, i, 3];

                    for (int c = 0; c < numClasses; c++)
                    {
                        float score = tensor[0, i, 4 + c];
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestClass = c;
                        }
                    }
                }

                var rect = ConvertBbox(bx0, bx1, bx2, bx3, originalWidth, originalHeight);
                string label = GetLabelName(bestClass);

                detections.Add(new LayoutDetection(rect, label, bestClass, bestScore));
            }
        }

        return detections;
    }

    private PdfRectangle ConvertBbox(float v0, float v1, float v2, float v3, int imageWidth, int imageHeight)
    {
        return _options.OutputBboxFormat switch
        {
            BboxFormat.CxCyWh => DetectionPostprocessing.CxCyWhToRect(v0, v1, v2, v3, imageWidth, imageHeight),
            BboxFormat.Xywh => DetectionPostprocessing.XyxyToRect(v0, v1, v0 + v2, v1 + v3, imageWidth, imageHeight),
            _ => DetectionPostprocessing.XyxyToRect(v0, v1, v2, v3, imageWidth, imageHeight)
        };
    }

    private string GetLabelName(int classId)
    {
        if (_options.ClassLabels is not null && _options.ClassLabels.TryGetValue(classId, out var name))
        {
            return name;
        }

        return $"class_{classId}";
    }

    private static int[]? ExtractIntArray(DisposableNamedOnnxValue value)
    {
        if (value.Value is Tensor<long> longTensor)
        {
            var dims = longTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var arr = new int[count];

            for (int i = 0; i < count; i++)
            {
                arr[i] = dims.Length > 1 ? (int)longTensor[0, i] : (int)longTensor[i];
            }

            return arr;
        }

        if (value.Value is Tensor<int> intTensor)
        {
            var dims = intTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var arr = new int[count];

            for (int i = 0; i < count; i++)
            {
                arr[i] = dims.Length > 1 ? intTensor[0, i] : intTensor[i];
            }

            return arr;
        }

        return null;
    }

    private static float[]? ExtractFloatArray(DisposableNamedOnnxValue value)
    {
        if (value.Value is Tensor<float> floatTensor)
        {
            var dims = floatTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var arr = new float[count];

            for (int i = 0; i < count; i++)
            {
                arr[i] = dims.Length > 1 ? floatTensor[0, i] : floatTensor[i];
            }

            return arr;
        }

        return null;
    }

    private static float[]? ExtractBoxArray(DisposableNamedOnnxValue value)
    {
        if (value.Value is Tensor<float> floatTensor)
        {
            var dims = floatTensor.Dimensions;
            int count = dims.Length > 1 ? dims[1] : dims[0];
            var arr = new float[count * 4];

            for (int i = 0; i < count; i++)
            {
                if (dims.Length == 3)
                {
                    arr[i * 4 + 0] = floatTensor[0, i, 0];
                    arr[i * 4 + 1] = floatTensor[0, i, 1];
                    arr[i * 4 + 2] = floatTensor[0, i, 2];
                    arr[i * 4 + 3] = floatTensor[0, i, 3];
                }
                else if (dims.Length == 2)
                {
                    arr[i * 4 + 0] = floatTensor[i, 0];
                    arr[i * 4 + 1] = floatTensor[i, 1];
                    arr[i * 4 + 2] = floatTensor[i, 2];
                    arr[i * 4 + 3] = floatTensor[i, 3];
                }
            }

            return arr;
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
