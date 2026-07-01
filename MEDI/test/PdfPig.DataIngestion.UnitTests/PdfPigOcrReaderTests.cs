#if NET8_0_OR_GREATER
#pragma warning disable CS1591
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Microsoft.Extensions.DataIngestion;
using Xunit;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

// Offline tests use a canned FakeOcrClient: no network, no secrets.
public class PdfPigOcrReaderTests
{
    // A canned document-native IOcrClient: one whole-document call returns all pages with real text + bbox.
    // Mirrors how Mistral OCR / Azure DI behave (the foundry-samples Mistral notebook hands over the whole PDF).
    private sealed class FakeOcrClient : IOcrClient
    {
        public int Calls { get; private set; }
        public string? LastMediaType { get; private set; }
        public int PageCount { get; init; } = 2;

        // When set, page 0 also carries a table. MarkdownOnly mirrors Mistral OCR (a markdown string, no cells);
        // otherwise a structured cell grid is returned (mirrors Azure DI).
        public bool IncludeTable { get; init; }
        public bool TableMarkdownOnly { get; init; } = true;

        public Task<OcrResult> GetTextAsync(
            Stream document, string mediaType, OcrOptions? options = null,
            IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMediaType = mediaType;
            var pages = new List<OcrPage>();
            for (var p = 0; p < PageCount; p++)
            {
                var block = new OcrBlock($"Recovered scanned text, page {p + 1}.")
                {
                    Kind = "paragraph",
                    BoundingRegion = OcrBoundingRegion.FromRectangle(p + 1, left: 10, top: 700, right: 500, bottom: 680)
                };

                var tables = new List<OcrTable>();
                if (IncludeTable && p == 0)
                {
                    tables.Add(MakeTable());
                }

                pages.Add(new OcrPage(p, $"Recovered scanned text, page {p + 1}.")
                {
                    Blocks = [block],
                    Tables = tables
                });
            }
            var result = new OcrResult(pages) { OcrSource = "vision_llm", ModelId = "fake-ocr" };
            return Task.FromResult(result);
        }

        private OcrTable MakeTable()
        {
            const string markdown = "| a | b |\n| - | - |\n| 1 | 2 |";
            OcrTableCell[,]? cells = null;
            if (!TableMarkdownOnly)
            {
                cells = new OcrTableCell[2, 2]
                {
                    { new OcrTableCell("a"), new OcrTableCell("b") },
                    { new OcrTableCell("1"), new OcrTableCell("2") }
                };
            }

            return new OcrTable(rowCount: 2, columnCount: 2, cells, markdown)
            {
                BoundingRegion = OcrBoundingRegion.FromRectangle(1, left: 20, top: 600, right: 480, bottom: 500),
                Confidence = 0.9
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private static List<IngestionDocumentParagraph> Paragraphs(IngestionDocument doc)
        => doc.Sections.SelectMany(s => s.Elements).OfType<IngestionDocumentParagraph>().ToList();

    private static List<IngestionDocumentTable> Tables(IngestionDocument doc)
        => doc.Sections.SelectMany(s => s.Elements).OfType<IngestionDocumentTable>().ToList();

    [Fact]
    public async Task AllPages_DocumentNative_ProducesRealOcrText_Inline_NoPlaceholder_NoEnricher()
    {
        var ocr = new FakeOcrClient { PageCount = 3 };
        var reader = new PdfPigOcrReader(ocr, OcrPolicy.AllPages);

        var path = IntegrationHelpers.GetDocumentPath("data");
        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var paragraphs = Paragraphs(doc);

        // Document-native: ONE whole-document call (not one per page), handed the original PDF media type.
        Assert.Equal(1, ocr.Calls);
        Assert.Equal("application/pdf", ocr.LastMediaType);

        // The provider's pages[] became sections; text is populated INLINE by the reader, not by an enricher.
        Assert.Equal(3, doc.Sections.Count);
        Assert.NotEmpty(paragraphs);
        Assert.All(paragraphs, p => Assert.False(string.IsNullOrWhiteSpace(p.Text)));
        Assert.Contains(paragraphs, p => p.Text == "Recovered scanned text, page 1.");

        // No "[scanned-page]" placeholder, no Metadata["placeholder"] -- the temporal coupling is gone.
        Assert.DoesNotContain(paragraphs, p => p.Metadata.ContainsKey("placeholder"));
        Assert.DoesNotContain(paragraphs, p => p.GetMarkdown() == "[scanned-page]");

        // Structured metadata flowed from the typed OcrBoundingRegion into the same BoundingBox.* keys.
        var ocrPara = paragraphs.First();
        Assert.Equal("vision_llm", ocrPara.Metadata["ocr_source"]);
        Assert.True(ocrPara.Metadata.ContainsKey("BoundingBox.Left"));
        Assert.True(ocrPara.Metadata.ContainsKey("BoundingBox.Right"));
        Assert.True(ocrPara.Metadata.ContainsKey("BoundingBox.Top"));
        Assert.True(ocrPara.Metadata.ContainsKey("BoundingBox.Bottom"));
    }

    [Fact]
    public async Task FallbackForEmptyPages_BornDigitalPdf_UsesNativeText_DoesNotCallOcr()
    {
        var ocr = new FakeOcrClient();
        var reader = new PdfPigOcrReader(ocr, OcrPolicy.FallbackForEmptyPages);

        var path = IntegrationHelpers.GetDocumentPath("data");
        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var paragraphs = Paragraphs(doc);

        // data.pdf has a text layer, so the native PdfPig path wins and OCR is never invoked.
        Assert.Equal(0, ocr.Calls);
        Assert.NotEmpty(paragraphs);
        Assert.All(paragraphs, p => Assert.False(string.IsNullOrWhiteSpace(p.Text)));
        Assert.DoesNotContain(paragraphs, p => p.Metadata.ContainsKey("ocr_source"));
    }

    [Fact]
    public async Task Never_RequiresNoOcrClient_AndNeverPlaceholders()
    {
        var reader = new PdfPigOcrReader(ocrClient: null, policy: OcrPolicy.Never);

        var path = IntegrationHelpers.GetDocumentPath("data");
        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var paragraphs = Paragraphs(doc);
        Assert.NotEmpty(paragraphs);
        Assert.DoesNotContain(paragraphs, p => p.Metadata.ContainsKey("placeholder"));
    }

    [Fact]
    public async Task AllPages_MarkdownOnlyTable_MapsToTypedIngestionDocumentTable_NotMarkdownParagraph()
    {
        // Mistral-shaped provider: a table arrives as a markdown string with no cell grid.
        var ocr = new FakeOcrClient { PageCount = 2, IncludeTable = true, TableMarkdownOnly = true };
        var reader = new PdfPigOcrReader(ocr, OcrPolicy.AllPages);

        var path = IntegrationHelpers.GetDocumentPath("data");
        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var tables = Tables(doc);

        // The table became a TYPED table element (the routing signal), not a markdown paragraph.
        var table = Assert.Single(tables);
        Assert.Equal("table", table.Metadata["element_type"]);
        Assert.Equal("vision_llm", table.Metadata["ocr_source"]);
        Assert.Contains("| a | b |", table.GetMarkdown());

        // Markdown-only provider: we carry the markdown but do NOT fabricate a cell grid.
        Assert.Empty(table.Cells);

        // Geometry + confidence flowed through the same telemetry keys the guidance names.
        Assert.True(table.Metadata.ContainsKey("BoundingBox.Left"));
        Assert.True(table.Metadata.ContainsKey("confidence"));
    }

    [Fact]
    public async Task AllPages_StructuredTable_PopulatesCellGrid()
    {
        // Azure-DI-shaped provider: the same OcrTable also carries a structured cell grid.
        var ocr = new FakeOcrClient { PageCount = 1, IncludeTable = true, TableMarkdownOnly = false };
        var reader = new PdfPigOcrReader(ocr, OcrPolicy.AllPages);

        var path = IntegrationHelpers.GetDocumentPath("data");
        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var table = Assert.Single(Tables(doc));

        // Cells are populated only because the provider supplied them (2x2).
        Assert.Equal(2, table.Cells.GetLength(0));
        Assert.Equal(2, table.Cells.GetLength(1));
        Assert.Equal("a", ((IngestionDocumentParagraph)table.Cells[0, 0]!).Text);
        Assert.Equal("2", ((IngestionDocumentParagraph)table.Cells[1, 1]!).Text);
    }

    [Fact]
    public async Task FallbackForEmptyPages_CustomPredicate_CanForceOcrOnPagesWithNativeText()
    {
        // Default predicate would skip born-digital pages; a caller predicate ("always OCR") overrides that.
        // This proves policy lives with the caller, not baked into the reader.
        SkiaNativeLibrary.EnsureAvailable();

        var ocr = new FakeOcrClient { PageCount = 1 };
        var reader = new PdfPigOcrReader(
            ocr, OcrPolicy.FallbackForEmptyPages, ocrPagePredicate: static _ => true);

        var path = IntegrationHelpers.GetDocumentPath("data");
        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        // data.pdf has native text, so the DEFAULT predicate would call OCR 0 times; the override forces it.
        Assert.True(ocr.Calls > 0);
        Assert.Contains(Paragraphs(doc), p => p.Metadata.TryGetValue("ocr_source", out var s) && (string?)s == "vision_llm");
    }
}
#endif
