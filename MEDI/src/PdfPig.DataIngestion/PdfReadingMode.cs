namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>
/// Controls how <see cref="PdfPigReader"/> extracts content from PDF pages.
/// </summary>
public enum PdfReadingMode
{
    /// <summary>
    /// Native text extraction with page segmentation. No page images are rendered.
    /// This is the fastest mode with zero external dependencies.
    /// Scanned pages with no extractable text produce empty sections.
    /// </summary>
    TextOnly,

    /// <summary>
    /// Native text extraction with page segmentation and page image rendering.
    /// Scanned pages that produce no text receive placeholder elements for
    /// downstream VLM-based OCR via <c>VisionOcrEnricher</c>.
    /// </summary>
    Hybrid,

    /// <summary>
    /// Skip native text extraction entirely. Every page is rendered as an image
    /// with a placeholder element. Requires a downstream <c>VisionOcrEnricher</c>
    /// with a vision-capable LLM to populate the text content.
    /// </summary>
    VisionOnly
}
