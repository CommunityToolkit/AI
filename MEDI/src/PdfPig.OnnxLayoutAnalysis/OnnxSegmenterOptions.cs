using Microsoft.ML.OnnxRuntime;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// Options for configuring the <see cref="OnnxPageSegmenter"/>.
/// </summary>
public record OnnxSegmenterOptions
{
    /// <summary>
    /// Minimum confidence threshold for detections (0.0 to 1.0).
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.3f;

    /// <summary>
    /// ONNX Runtime session options. Use to configure GPU, thread count, etc.
    /// </summary>
    public SessionOptions? SessionOptions { get; set; }

    /// <summary>
    /// DPI for rendering the page image. Higher values improve accuracy but are slower.
    /// </summary>
    public int RenderDpi { get; set; } = 150;
}
