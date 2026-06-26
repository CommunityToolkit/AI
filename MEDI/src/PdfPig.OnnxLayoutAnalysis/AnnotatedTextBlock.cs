using System.Collections.Generic;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// A <see cref="TextBlock"/> that carries the layout detection label and confidence
/// from an ONNX model. Downstream code that only knows about <see cref="TextBlock"/>
/// sees a regular block; callers that know about this subclass can read <see cref="Label"/>
/// and <see cref="Confidence"/>.
/// </summary>
public sealed class AnnotatedTextBlock : TextBlock
{
    /// <summary>
    /// The element type label detected by the ONNX layout model
    /// (e.g. "table", "picture", "section_header", "text").
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// The model's confidence score for this detection, between 0 and 1.
    /// </summary>
    public float Confidence { get; }

    /// <summary>
    /// Create a new <see cref="AnnotatedTextBlock"/>.
    /// </summary>
    /// <param name="lines">The text lines in this block.</param>
    /// <param name="label">The layout detection label.</param>
    /// <param name="confidence">The detection confidence score.</param>
    /// <param name="separator">The separator used between lines.</param>
    public AnnotatedTextBlock(
        IReadOnlyList<TextLine> lines,
        string label,
        float confidence,
        string separator = "\n")
        : base(lines, separator)
    {
        Label = label;
        Confidence = confidence;
    }
}
