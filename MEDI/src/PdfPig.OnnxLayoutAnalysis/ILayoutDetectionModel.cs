using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// Strategy interface encapsulating model-specific preprocessing and postprocessing
/// for ONNX-based layout detection models.
/// </summary>
public interface ILayoutDetectionModel : IDisposable
{
    /// <summary>
    /// Convert a page image to the model's expected input tensor(s).
    /// </summary>
    /// <param name="pageImage">The rendered page image.</param>
    /// <param name="originalWidth">Original page image width in pixels.</param>
    /// <param name="originalHeight">Original page image height in pixels.</param>
    /// <returns>Named ONNX values ready for inference.</returns>
    IReadOnlyList<NamedOnnxValue> Preprocess(SKBitmap pageImage, int originalWidth, int originalHeight);

    /// <summary>
    /// Parse model output into layout detections.
    /// </summary>
    /// <param name="results">The raw ONNX inference results.</param>
    /// <param name="originalWidth">Original page image width in pixels.</param>
    /// <param name="originalHeight">Original page image height in pixels.</param>
    /// <returns>Detected layout elements.</returns>
    IReadOnlyList<LayoutDetection> Postprocess(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, int originalWidth, int originalHeight);

    /// <summary>
    /// Mapping from class ID to human-readable label name.
    /// </summary>
    IReadOnlyDictionary<int, string> LabelMapping { get; }

    /// <summary>
    /// Path to the ONNX model file on disk.
    /// </summary>
    string ModelPath { get; }
}
