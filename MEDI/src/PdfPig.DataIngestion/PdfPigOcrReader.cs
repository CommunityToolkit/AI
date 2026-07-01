#pragma warning disable CS1591 // vendored OCR shape, doc comments not required
// PdfPig reader replatformed onto a composed IOcrClient.
// It keeps native PdfPig text extraction while routing OCR through the provider-neutral IOcrClient seam:
//   - Native PdfPig text extraction stays intact (segmenter + bbox + element_type).
//   - Model-choice flags are replaced by OcrPolicy, a pure WHEN-to-OCR policy.
//   - The scanned-page path calls an injected IOcrClient INLINE and produces REAL text + structured
//     metadata. No "[scanned-page]" placeholder, no page_image stash, no temporal coupling to a downstream enricher.
//   - The reader depends on IOcrClient ONLY -- ZERO direct IChatClient. The vision-LLM path becomes one
//     IOcrClient provider (VisionLlmOcrClient), swappable with Mistral OCR / Azure DI.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>When the reader invokes the injected <see cref="IOcrClient"/>. A WHEN-to-OCR policy, NOT a model choice.</summary>
public enum OcrPolicy
{
    /// <summary>Never call OCR. Native PdfPig text only (the old <c>TextOnly</c>).</summary>
    Never,

    /// <summary>
    /// Native text first; OCR only the pages that yield no extractable text (the old <c>Hybrid</c>).
    /// Image-per-page archetype: each empty page is rendered and sent to <see cref="IOcrClient"/> as an image.
    /// </summary>
    FallbackForEmptyPages,

    /// <summary>
    /// OCR everything (the old <c>VisionOnly</c>, minus the model lock-in). Document-native archetype: the
    /// whole document is handed to <see cref="IOcrClient"/> in one call, and its <c>pages[]</c> become sections
    /// (how Mistral OCR / Azure DI work). The <see cref="IOcrClient"/> shape is general enough to serve both.
    /// </summary>
    AllPages
}

/// <summary>
/// Per-page context the OCR policy predicate sees, so the caller can decide (not the library) whether a
/// page needs OCR, using the extraction telemetry. This is the "policy in the caller's hands, mechanism in
/// the reader" seam: the enum is sugar for the common cases, the predicate is the escape hatch.
/// </summary>
public readonly record struct PageOcrContext(int PageNumber, int NativeElementCount)
{
    /// <summary>Whether native (PdfPig) text extraction produced any elements for this page.</summary>
    public bool HasNativeText => NativeElementCount > 0;
}

/// <summary>
/// PdfPig-backed MEDI reader that composes an injected <see cref="IOcrClient"/> for scanned/image content.
///
/// This reader supersedes the old reading-mode / placeholder / downstream-enricher flow:
/// <c>OcrPolicy.Never</c> keeps native text extraction only, while OCR is composed inline when requested.
/// </summary>
public sealed class PdfPigOcrReader : IngestionDocumentReader
{
    private readonly IPageSegmenter segmenter;
    private readonly IOcrClient? ocrClient;
    private readonly OcrPolicy policy;
    private readonly int renderDpi;
    private readonly Func<UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock, string?>? elementTypeResolver;
    private readonly Func<PageOcrContext, bool>? ocrPagePredicate;

    public PdfPigOcrReader(
        IOcrClient? ocrClient = null,
        OcrPolicy policy = OcrPolicy.Never,
        IPageSegmenter? segmenter = null,
        int renderDpi = 150,
        Func<UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock, string?>? elementTypeResolver = null,
        Func<PageOcrContext, bool>? ocrPagePredicate = null)
    {
        if (policy != OcrPolicy.Never && ocrClient is null)
        {
            throw new ArgumentNullException(nameof(ocrClient), $"An {nameof(IOcrClient)} is required when policy is {policy}.");
        }

        this.ocrClient = ocrClient;
        this.policy = policy;
        this.segmenter = segmenter ?? DefaultPageSegmenter.Instance;
        this.renderDpi = renderDpi;
        this.elementTypeResolver = elementTypeResolver;
        this.ocrPagePredicate = ocrPagePredicate;
    }

    public override async Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        // Buffer once: we may hand the whole document to the OCR client (document-native archetype) AND
        // open it with PdfPig (native text / per-page rendering).
        byte[] bytes;
        if (source is MemoryStream existing)
        {
            bytes = existing.ToArray();
        }
        else
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }

        var document = new IngestionDocument(identifier);

        // AllPages == OCR everything. For a document-native OCR provider (Mistral OCR, Azure DI) the natural
        // call is ONE whole-document request that returns pages[] -- exactly what the foundry-samples Mistral
        // notebook does. Hand the original document to IOcrClient and map its pages to sections.
        if (policy == OcrPolicy.AllPages && ocrClient is not null)
        {
            using var docStream = new MemoryStream(bytes, writable: false);
            var docResult = await ocrClient.GetTextAsync(
                docStream, mediaType, cancellationToken: cancellationToken).ConfigureAwait(false);

            for (var p = 0; p < docResult.Pages.Count; p++)
            {
                var section = new IngestionDocumentSection { PageNumber = p + 1 };
                AppendOcrPage(section, docResult.Pages[p], docResult.OcrSource, p + 1);
                document.Sections.Add(section);
            }

            return document;
        }

        // Otherwise: native PdfPig text, with image-per-page OCR only for the pages that need it
        // (FallbackForEmptyPages == the old Hybrid). This is the image-per-page archetype (vision-LLM style).
        using var pdfDocument = PdfDocument.Open(new MemoryStream(bytes, writable: false));

        for (var i = 1; i <= pdfDocument.NumberOfPages; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdfDocument.GetPage(i);
            var section = new IngestionDocumentSection { PageNumber = i };

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

            // Fallback OCR: the caller's predicate decides per page (default = OCR only pages with no native
            // text, the old Hybrid behavior). Policy lives with the caller; the reader supplies the telemetry.
            if (policy == OcrPolicy.FallbackForEmptyPages && ocrClient is not null)
            {
                var context = new PageOcrContext(i, section.Elements.Count);
                var shouldOcr = ocrPagePredicate ?? (static ctx => !ctx.HasNativeText);

                if (shouldOcr(context))
                {
                    var imageBytes = PageImageRenderer.RenderPage(page, renderDpi);
                    using var imageStream = new MemoryStream(imageBytes);

                    var result = await ocrClient.GetTextAsync(
                        imageStream, "image/png", cancellationToken: cancellationToken).ConfigureAwait(false);

                    var ocrPage = result.Pages.Count > 0 ? result.Pages[0] : null;
                    if (ocrPage is not null)
                    {
                        AppendOcrPage(section, ocrPage, result.OcrSource, i);
                    }
                }
            }

            document.Sections.Add(section);
        }

        return document;
    }

    // Map one OcrPage -> real MEDI elements. No placeholder; text is populated here, not by an enricher.
    private static void AppendOcrPage(IngestionDocumentSection section, OcrPage page, string? ocrSource, int pageNumber)
    {
        if (page.Blocks.Count > 0)
        {
            foreach (var block in page.Blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text))
                {
                    continue;
                }

                var paragraph = new IngestionDocumentParagraph(block.Text)
                {
                    Text = block.Text,
                    PageNumber = pageNumber
                };

                if (block.BoundingRegion is { } region)
                {
                    var (left, top, right, bottom) = region.GetBounds();
                    paragraph.Metadata["BoundingBox.Left"] = left;
                    paragraph.Metadata["BoundingBox.Top"] = top;
                    paragraph.Metadata["BoundingBox.Right"] = right;
                    paragraph.Metadata["BoundingBox.Bottom"] = bottom;
                }

                if (block.Kind is not null)
                {
                    paragraph.Metadata["element_type"] = block.Kind;
                }

                if (ocrSource is not null)
                {
                    paragraph.Metadata["ocr_source"] = ocrSource;
                }

                section.Elements.Add(paragraph);
            }
        }
        else if (!string.IsNullOrWhiteSpace(page.Markdown))
        {
            var paragraph = new IngestionDocumentParagraph(page.Markdown)
            {
                Text = page.Markdown,
                PageNumber = pageNumber
            };

            if (ocrSource is not null)
            {
                paragraph.Metadata["ocr_source"] = ocrSource;
            }

            section.Elements.Add(paragraph);
        }

        // Tables map to the typed IngestionDocumentTable, not to markdown paragraphs. The type IS the routing
        // signal (chunker, retrieval filters, VisionTableEnricher all key off it). Markdown always; the cell
        // grid only when the provider supplies it -- graceful degradation applied to tables.
        foreach (var table in page.Tables)
        {
            var cells = MapCells(table.Cells);
            var tableElement = new IngestionDocumentTable(table.ToMarkdown(), cells)
            {
                Text = table.ToMarkdown(),
                PageNumber = pageNumber
            };

            tableElement.Metadata["element_type"] = "table";

            if (table.BoundingRegion is { } region)
            {
                var (left, top, right, bottom) = region.GetBounds();
                tableElement.Metadata["BoundingBox.Left"] = left;
                tableElement.Metadata["BoundingBox.Top"] = top;
                tableElement.Metadata["BoundingBox.Right"] = right;
                tableElement.Metadata["BoundingBox.Bottom"] = bottom;
            }

            if (table.Confidence is { } confidence)
            {
                tableElement.Metadata["confidence"] = confidence;
            }

            if (ocrSource is not null)
            {
                tableElement.Metadata["ocr_source"] = ocrSource;
            }

            section.Elements.Add(tableElement);
        }
    }

    // Structured cells when the provider has them; an empty grid otherwise (markdown-only tables). We never
    // fabricate structure the provider did not return.
    private static IngestionDocumentElement[,] MapCells(OcrTableCell[,]? cells)
    {
        if (cells is null)
        {
            return new IngestionDocumentElement[0, 0];
        }

        var rows = cells.GetLength(0);
        var cols = cells.GetLength(1);
        var mapped = new IngestionDocumentElement[rows, cols];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var text = cells[r, c]?.Text ?? string.Empty;
                mapped[r, c] = new IngestionDocumentParagraph(text) { Text = text };
            }
        }

        return mapped;
    }
}
