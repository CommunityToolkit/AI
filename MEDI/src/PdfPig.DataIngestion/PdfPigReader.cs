using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>
/// Reads PDF documents using PdfPig and converts them to MEDI <see cref="IngestionDocument"/> format.
/// Supports pluggable page segmentation via <see cref="IPageSegmenter"/> and configurable
/// reading modes via <see cref="PdfReadingMode"/>.
/// </summary>
public sealed class PdfPigReader : IngestionDocumentReader
{
    private readonly IPageSegmenter segmenter;
    private readonly PdfReadingMode mode;
    private readonly int renderDpi;
    private readonly Func<TextBlock, string?>? elementTypeResolver;

    /// <summary>
    /// Creates a new <see cref="PdfPigReader"/> using dependency-injected options.
    /// </summary>
    /// <param name="options">The configured reader options.</param>
    /// <param name="segmenter">
    /// Page segmenter for layout analysis. Defaults to <see cref="DefaultPageSegmenter"/> if <see langword="null"/>.
    /// </param>
    /// <param name="elementTypeResolver">
    /// Optional delegate that resolves the element type label from a <see cref="TextBlock"/>.
    /// </param>
    public PdfPigReader(
        IOptions<PdfPigReaderOptions> options,
        IPageSegmenter? segmenter = null,
        Func<TextBlock, string?>? elementTypeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;
        this.segmenter = segmenter ?? DefaultPageSegmenter.Instance;
        this.mode = opts.Mode;
        this.renderDpi = opts.RenderDpi;
        this.elementTypeResolver = elementTypeResolver;
    }

    /// <summary>
    /// Creates a new <see cref="PdfPigReader"/>.
    /// </summary>
    /// <param name="segmenter">
    /// Page segmenter for layout analysis. Defaults to <see cref="DefaultPageSegmenter"/> if <see langword="null"/>.
    /// Ignored when <paramref name="mode"/> is <see cref="PdfReadingMode.VisionOnly"/>.
    /// </param>
    /// <param name="mode">
    /// Controls the text extraction strategy. Defaults to <see cref="PdfReadingMode.TextOnly"/>.
    /// </param>
    /// <param name="renderDpi">
    /// The DPI to use when rendering page images. Applies to <see cref="PdfReadingMode.Hybrid"/>
    /// and <see cref="PdfReadingMode.VisionOnly"/> modes. Defaults to 150.
    /// </param>
    /// <param name="elementTypeResolver">
    /// Optional delegate that resolves the element type label from a <see cref="TextBlock"/>.
    /// When provided, the resolved type is stored in element metadata as "element_type".
    /// This decouples the reader from specific segmenter implementations (e.g. ONNX).
    /// </param>
    public PdfPigReader(
        IPageSegmenter? segmenter = null,
        PdfReadingMode mode = PdfReadingMode.TextOnly,
        int renderDpi = 150,
        Func<TextBlock, string?>? elementTypeResolver = null)
    {
        this.segmenter = segmenter ?? DefaultPageSegmenter.Instance;
        this.mode = mode;
        this.renderDpi = renderDpi;
        this.elementTypeResolver = elementTypeResolver;
    }

    /// <inheritdoc/>
    public override Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        using var pdfDocument = PdfDocument.Open(source);
        var document = new IngestionDocument(identifier);

        for (var i = 1; i <= pdfDocument.NumberOfPages; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdfDocument.GetPage(i);

            var section = new IngestionDocumentSection
            {
                PageNumber = i
            };

            var renderImages = mode is PdfReadingMode.Hybrid or PdfReadingMode.VisionOnly;

            if (renderImages)
            {
                var imageBytes = PageImageRenderer.RenderPage(page, renderDpi);
                section.Metadata["page_image"] = imageBytes;
                section.Metadata["page_width"] = page.Width;
                section.Metadata["page_height"] = page.Height;
            }

            if (mode is not PdfReadingMode.VisionOnly)
            {
                var words = page.GetWords();
                var blocks = segmenter.GetBlocks(words);

                foreach (var block in blocks)
                {
                    if (string.IsNullOrEmpty(block.Text))
                    {
                        continue;
                    }

                    var paragraph = new IngestionDocumentParagraph(block.Text)
                    {
                        Text = block.Text,
                        PageNumber = i
                    };

                    var bbox = block.BoundingBox;
                    paragraph.Metadata["BoundingBox.Left"] = bbox.Left;
                    paragraph.Metadata["BoundingBox.Bottom"] = bbox.Bottom;
                    paragraph.Metadata["BoundingBox.Right"] = bbox.Right;
                    paragraph.Metadata["BoundingBox.Top"] = bbox.Top;

                    var elementType = elementTypeResolver?.Invoke(block);
                    if (elementType is not null)
                    {
                        paragraph.Metadata["element_type"] = elementType;
                    }

                    section.Elements.Add(paragraph);
                }
            }

            // For scanned/image-only pages (or VisionOnly mode) with no elements,
            // create a placeholder so VisionOcrEnricher can process the page image.
            if (section.Elements.Count == 0 && renderImages)
            {
                var placeholder = new IngestionDocumentParagraph("[scanned-page]")
                {
                    Text = string.Empty,
                    PageNumber = i
                };
                placeholder.Metadata["placeholder"] = true;
                section.Elements.Add(placeholder);
            }

            document.Sections.Add(section);
        }

        return Task.FromResult(document);
    }
}
