#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Xunit;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

public class PdfPigReaderTests
{
    #region Default (TextOnly) mode

    [Fact]
    public async Task ReadAsync_DefaultMode_ReturnsDocumentWithSections()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        Assert.NotNull(doc);
        Assert.Equal("data.pdf", doc.Identifier);
        Assert.NotEmpty(doc.Sections);
    }

    [Fact]
    public async Task ReadAsync_DefaultMode_SectionCountMatchesPageCount()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("cat-genetics");

        int expectedPages;
        using (var pdfDoc = PdfDocument.Open(path))
        {
            expectedPages = pdfDoc.NumberOfPages;
        }

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "cat-genetics.pdf", "application/pdf");

        Assert.Equal(expectedPages, doc.Sections.Count);
    }

    [Fact]
    public async Task ReadAsync_DefaultMode_SectionsContainCorrectPageNumbers()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        for (int i = 0; i < doc.Sections.Count; i++)
        {
            Assert.Equal(i + 1, doc.Sections[i].PageNumber);
        }
    }

    [Fact]
    public async Task ReadAsync_DefaultMode_ParagraphsContainBoundingBoxMetadata()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var elementsWithMetadata = doc.EnumerateContent()
            .Where(e => e.HasMetadata)
            .ToList();

        Assert.NotEmpty(elementsWithMetadata);

        var first = elementsWithMetadata.First();
        Assert.True(first.Metadata.ContainsKey("BoundingBox.Left"));
        Assert.True(first.Metadata.ContainsKey("BoundingBox.Bottom"));
        Assert.True(first.Metadata.ContainsKey("BoundingBox.Right"));
        Assert.True(first.Metadata.ContainsKey("BoundingBox.Top"));
    }

    [Fact]
    public async Task ReadAsync_DefaultMode_BoundingBoxValuesAreNumeric()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var element = doc.EnumerateContent().First(e => e.HasMetadata);

        Assert.IsType<double>(element.Metadata["BoundingBox.Left"]);
        Assert.IsType<double>(element.Metadata["BoundingBox.Bottom"]);
        Assert.IsType<double>(element.Metadata["BoundingBox.Right"]);
        Assert.IsType<double>(element.Metadata["BoundingBox.Top"]);
    }

    [Fact]
    public async Task ReadAsync_DefaultMode_ParagraphsHaveNonEmptyText()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var allElements = doc.EnumerateContent().ToList();

        foreach (var element in allElements)
        {
            Assert.False(string.IsNullOrEmpty(element.Text),
                "All paragraphs should have non-empty text (empty blocks are skipped).");
        }
    }

    [Fact]
    public async Task ReadAsync_DefaultMode_ParagraphPageNumbersMatchSectionPageNumbers()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        foreach (var section in doc.Sections)
        {
            foreach (var element in section.Elements)
            {
                Assert.Equal(section.PageNumber, element.PageNumber);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_WithExplicitSegmenter_ReturnsStructuredSections()
    {
        var reader = new PdfPigReader(RecursiveXYCut.Instance);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        Assert.NotNull(doc);
        Assert.NotEmpty(doc.Sections);
    }

    [Fact]
    public async Task ReadAsync_MinimalPdf_ReturnsDocument()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("inherited_mediabox");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "inherited_mediabox.pdf", "application/pdf");

        Assert.NotNull(doc);
        Assert.Equal("inherited_mediabox.pdf", doc.Identifier);
    }

    [Fact]
    public async Task ReadAsync_CancellationAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var reader = new PdfPigReader();
        var path = IntegrationHelpers.GetDocumentPath("data");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var stream = File.OpenRead(path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(stream, "data.pdf", "application/pdf", cts.Token));
    }

    #endregion

    #region TextOnly mode

    [Fact]
    public async Task ReadAsync_TextOnly_DoesNotStorePageImage()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.TextOnly);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        foreach (var section in doc.Sections)
        {
            Assert.False(section.Metadata.ContainsKey("page_image"),
                $"Section for page {section.PageNumber} should NOT contain page_image in TextOnly mode.");
            Assert.False(section.Metadata.ContainsKey("page_width"));
            Assert.False(section.Metadata.ContainsKey("page_height"));
        }
    }

    [Fact]
    public async Task ReadAsync_TextOnly_BlankPage_NoPlaceholder()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.TextOnly);
        var pdfBytes = CreateBlankPagePdf();

        using var stream = new MemoryStream(pdfBytes);
        var doc = await reader.ReadAsync(stream, "blank.pdf", "application/pdf");

        Assert.Single(doc.Sections);
        Assert.Empty(doc.Sections[0].Elements);
    }

    #endregion

    #region Hybrid mode

    [Fact]
    public async Task ReadAsync_Hybrid_StoresPageImageInSectionMetadata()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        foreach (var section in doc.Sections)
        {
            Assert.True(section.Metadata.ContainsKey("page_image"),
                $"Section for page {section.PageNumber} should contain page_image in Hybrid mode.");
        }
    }

    [Fact]
    public async Task ReadAsync_Hybrid_PageImageIsValidPng()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var section = doc.Sections[0];
        var imageBytes = section.Metadata["page_image"] as byte[];

        Assert.NotNull(imageBytes);
        Assert.True(imageBytes.Length > 4);
        // PNG magic bytes
        Assert.Equal(0x89, imageBytes[0]);
        Assert.Equal(0x50, imageBytes[1]);
        Assert.Equal(0x4E, imageBytes[2]);
        Assert.Equal(0x47, imageBytes[3]);
    }

    [Fact]
    public async Task ReadAsync_Hybrid_StoresPageDimensions()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        var section = doc.Sections[0];
        Assert.True(section.Metadata.ContainsKey("page_width"));
        Assert.True(section.Metadata.ContainsKey("page_height"));
        Assert.IsType<double>(section.Metadata["page_width"]);
        Assert.IsType<double>(section.Metadata["page_height"]);
    }

    [Fact]
    public async Task ReadAsync_Hybrid_CustomDpi_ProducesValidDocument()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid, renderDpi: 72);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        Assert.NotNull(doc);
        Assert.NotEmpty(doc.Sections);

        var imageBytes = doc.Sections[0].Metadata["page_image"] as byte[];
        Assert.NotNull(imageBytes);
        Assert.Equal(0x89, imageBytes[0]);
        Assert.Equal(0x50, imageBytes[1]);
    }

    [Fact]
    public async Task ReadAsync_Hybrid_WithAllParameters_ProducesValidDocument()
    {
        var reader = new PdfPigReader(
            segmenter: RecursiveXYCut.Instance,
            mode: PdfReadingMode.Hybrid,
            renderDpi: 200);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        Assert.NotNull(doc);
        Assert.NotEmpty(doc.Sections);
        Assert.True(doc.Sections[0].Metadata.ContainsKey("page_image"));
    }

    [Fact]
    public async Task ReadAsync_Hybrid_BlankPage_CreatesPlaceholderElement()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var pdfBytes = CreateBlankPagePdf();

        using var stream = new MemoryStream(pdfBytes);
        var doc = await reader.ReadAsync(stream, "blank.pdf", "application/pdf");

        Assert.Single(doc.Sections);
        var section = doc.Sections[0];
        Assert.Single(section.Elements);

        var element = section.Elements[0];
        Assert.Equal(string.Empty, element.Text);
    }

    [Fact]
    public async Task ReadAsync_Hybrid_BlankPage_PlaceholderHasCorrectPageNumber()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var pdfBytes = CreateBlankPagePdf();

        using var stream = new MemoryStream(pdfBytes);
        var doc = await reader.ReadAsync(stream, "blank.pdf", "application/pdf");

        var element = doc.Sections[0].Elements[0];
        Assert.Equal(1, element.PageNumber);
    }

    [Fact]
    public async Task ReadAsync_Hybrid_BlankPage_PlaceholderHasPlaceholderMetadata()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var pdfBytes = CreateBlankPagePdf();

        using var stream = new MemoryStream(pdfBytes);
        var doc = await reader.ReadAsync(stream, "blank.pdf", "application/pdf");

        var element = doc.Sections[0].Elements[0];
        Assert.True(element.HasMetadata);
        Assert.True(element.Metadata.ContainsKey("placeholder"));
        Assert.Equal(true, element.Metadata["placeholder"]);
    }

    [Fact]
    public async Task ReadAsync_Hybrid_BlankPage_SectionStillHasPageImage()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var pdfBytes = CreateBlankPagePdf();

        using var stream = new MemoryStream(pdfBytes);
        var doc = await reader.ReadAsync(stream, "blank.pdf", "application/pdf");

        var section = doc.Sections[0];
        Assert.True(section.Metadata.ContainsKey("page_image"));
        var imageBytes = section.Metadata["page_image"] as byte[];
        Assert.NotNull(imageBytes);
        Assert.True(imageBytes.Length > 0);
    }

    [Fact]
    public async Task ReadAsync_Hybrid_MixedPdf_OnlyBlankPagesGetPlaceholders()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.Hybrid);
        var pdfBytes = CreateMixedPdf();

        using var stream = new MemoryStream(pdfBytes);
        var doc = await reader.ReadAsync(stream, "mixed.pdf", "application/pdf");

        Assert.Equal(2, doc.Sections.Count);

        // Page 1 has text — should have normal elements, no placeholder
        var page1 = doc.Sections[0];
        Assert.NotEmpty(page1.Elements);
        foreach (var el in page1.Elements)
        {
            Assert.False(string.IsNullOrEmpty(el.Text));
            Assert.False(el.Metadata.ContainsKey("placeholder"));
        }

        // Page 2 is blank — should have single placeholder
        var page2 = doc.Sections[1];
        Assert.Single(page2.Elements);
        Assert.Equal(string.Empty, page2.Elements[0].Text);
        Assert.True(page2.Elements[0].Metadata.ContainsKey("placeholder"));
        Assert.Equal(2, page2.Elements[0].PageNumber);
    }

    #endregion

    #region VisionOnly mode

    [Fact]
    public async Task ReadAsync_VisionOnly_EveryPageGetsPlaceholder()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.VisionOnly);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        Assert.NotEmpty(doc.Sections);

        foreach (var section in doc.Sections)
        {
            Assert.Single(section.Elements);
            var element = section.Elements[0];
            Assert.Equal(string.Empty, element.Text);
            Assert.True(element.Metadata.ContainsKey("placeholder"));
            Assert.Equal(true, element.Metadata["placeholder"]);
        }
    }

    [Fact]
    public async Task ReadAsync_VisionOnly_AllSectionsHavePageImage()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.VisionOnly);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        foreach (var section in doc.Sections)
        {
            Assert.True(section.Metadata.ContainsKey("page_image"));
            var imageBytes = section.Metadata["page_image"] as byte[];
            Assert.NotNull(imageBytes);
            Assert.True(imageBytes.Length > 0);
        }
    }

    [Fact]
    public async Task ReadAsync_VisionOnly_PlaceholderPageNumbersMatchSections()
    {
        var reader = new PdfPigReader(mode: PdfReadingMode.VisionOnly);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        for (int i = 0; i < doc.Sections.Count; i++)
        {
            Assert.Equal(i + 1, doc.Sections[i].Elements[0].PageNumber);
        }
    }

    [Fact]
    public async Task ReadAsync_VisionOnly_SkipsTextExtraction()
    {
        // VisionOnly should produce placeholders even for PDFs with text
        var reader = new PdfPigReader(mode: PdfReadingMode.VisionOnly);
        var path = IntegrationHelpers.GetDocumentPath("data");

        using var stream = File.OpenRead(path);
        var doc = await reader.ReadAsync(stream, "data.pdf", "application/pdf");

        // All elements should be placeholders with empty text — no native text extraction
        foreach (var element in doc.EnumerateContent())
        {
            Assert.Equal(string.Empty, element.Text);
            Assert.True(element.Metadata.ContainsKey("placeholder"));
        }
    }

    #endregion

    #region Helpers

    private static byte[] CreateBlankPagePdf()
    {
        using var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }

    private static byte[] CreateMixedPdf()
    {
        using var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        // Page 1: has text
        var page1 = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        page1.AddText("Hello World", 12, new UglyToad.PdfPig.Core.PdfPoint(72, 720), font);
        // Page 2: blank (no text)
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }

    #endregion
}
#endif
