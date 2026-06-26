using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// A single detected layout element from an ONNX model.
/// </summary>
public record LayoutDetection(
    PdfRectangle BoundingBox,
    string Label,
    int ClassId,
    float Confidence);
