namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>
/// Options for configuring <see cref="PdfPigReader"/>.
/// </summary>
public record PdfPigReaderOptions
{
    /// <summary>
    /// Controls the text extraction strategy. Defaults to <see cref="PdfReadingMode.TextOnly"/>.
    /// </summary>
    public PdfReadingMode Mode { get; set; } = PdfReadingMode.TextOnly;

    /// <summary>
    /// The DPI to use when rendering page images. Applies to <see cref="PdfReadingMode.Hybrid"/>
    /// and <see cref="PdfReadingMode.VisionOnly"/> modes. Defaults to 150.
    /// </summary>
    public int RenderDpi { get; set; } = 150;
}
