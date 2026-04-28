using System.Collections.Generic;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.Models;

/// <summary>
/// Configuration options for a generic layout detection model,
/// driving the <see cref="ConfigurableLayoutModel"/>.
/// </summary>
public record LayoutModelOptions
{
    /// <summary>
    /// Expected model input width in pixels.
    /// </summary>
    public int InputWidth { get; init; } = 640;

    /// <summary>
    /// Expected model input height in pixels.
    /// </summary>
    public int InputHeight { get; init; } = 640;

    /// <summary>
    /// How the image should be resized before feeding to the model.
    /// </summary>
    public ResizeMode Resize { get; init; } = ResizeMode.Exact;

    /// <summary>
    /// Pixel format and data type for the input tensor.
    /// </summary>
    public PixelFormat PixelFormat { get; init; } = PixelFormat.Uint8Chw;

    /// <summary>
    /// Optional per-channel normalization to apply after converting to float.
    /// </summary>
    public ImageNormalization? Normalization { get; init; }

    /// <summary>
    /// Minimum confidence threshold for detections.
    /// </summary>
    public float ConfidenceThreshold { get; init; } = 0.3f;

    /// <summary>
    /// IoU threshold for Non-Maximum Suppression.
    /// </summary>
    public float NmsIouThreshold { get; init; } = 0.45f;

    /// <summary>
    /// Whether NMS should be applied to the output detections.
    /// </summary>
    public bool RequiresNms { get; init; }

    /// <summary>
    /// Format of the bounding box coordinates in the model output.
    /// </summary>
    public BboxFormat OutputBboxFormat { get; init; } = BboxFormat.CxCyWh;

    /// <summary>
    /// Mapping from class ID to human-readable label. If null, numeric labels are used.
    /// </summary>
    public IReadOnlyDictionary<int, string>? ClassLabels { get; init; }
}

/// <summary>
/// How the input image is resized before inference.
/// </summary>
public enum ResizeMode
{
    /// <summary>Stretch to exact target dimensions.</summary>
    Exact,

    /// <summary>Preserve aspect ratio and pad with a solid color.</summary>
    Letterbox,

    /// <summary>Preserve aspect ratio without padding (model must accept variable sizes).</summary>
    AspectPreserve
}

/// <summary>
/// Pixel format and data type for the model input tensor.
/// </summary>
public enum PixelFormat
{
    /// <summary>Unsigned 8-bit integer, CHW layout [1, 3, H, W].</summary>
    Uint8Chw,

    /// <summary>32-bit float, CHW layout [1, 3, H, W], values in [0, 1].</summary>
    Float32Chw
}

/// <summary>
/// Format of bounding box coordinates in the model output.
/// </summary>
public enum BboxFormat
{
    /// <summary>Center-X, Center-Y, Width, Height.</summary>
    CxCyWh,

    /// <summary>Top-left X, top-left Y, bottom-right X, bottom-right Y.</summary>
    Xyxy,

    /// <summary>Top-left X, top-left Y, Width, Height.</summary>
    Xywh
}

/// <summary>
/// Per-channel normalization parameters: (value - mean) / std.
/// </summary>
/// <param name="Mean">Per-channel mean values (RGB order, length 3).</param>
/// <param name="Std">Per-channel standard deviation values (RGB order, length 3).</param>
public record ImageNormalization(float[] Mean, float[] Std);

/// <summary>
/// Well-known normalization presets for common model architectures.
/// </summary>
public static class WellKnownNormalizations
{
    /// <summary>
    /// ImageNet normalization: mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225].
    /// </summary>
    public static readonly ImageNormalization ImageNet = new([0.485f, 0.456f, 0.406f], [0.229f, 0.224f, 0.225f]);

    /// <summary>
    /// Simple [0, 255] to [0, 1] normalization (mean=0, std=1/255).
    /// </summary>
    public static readonly ImageNormalization ZeroToOne = new([0f, 0f, 0f], [1f / 255f, 1f / 255f, 1f / 255f]);
}
