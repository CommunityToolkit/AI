#if NET8_0_OR_GREATER
using System;
using System.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Xunit;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

public class PageImageRendererTests
{
    [Fact]
    public void RenderPage_NullPage_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PageImageRenderer.RenderPage(null!));
    }

    [Fact]
    public void RenderRegion_NullPage_ThrowsArgumentNullException()
    {
        var region = new PdfRectangle(0, 0, 100, 100);
        Assert.Throws<ArgumentNullException>(() => PageImageRenderer.RenderRegion(null!, region));
    }

    [Fact]
    public void RenderPage_ReturnsValidPng()
    {
        var path = IntegrationHelpers.GetDocumentPath("data");
        using var pdfDoc = PdfDocument.Open(path);
        var page = pdfDoc.GetPage(1);

        var result = PageImageRenderer.RenderPage(page);

        Assert.NotNull(result);
        Assert.True(result.Length > 4, "PNG output should have more than 4 bytes.");
        // PNG magic bytes: 0x89 0x50 0x4E 0x47
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
        Assert.Equal(0x4E, result[2]);
        Assert.Equal(0x47, result[3]);
    }

    [Fact]
    public void RenderPage_DifferentDpiProducesDifferentSizedOutput()
    {
        var path = IntegrationHelpers.GetDocumentPath("data");
        using var pdfDoc = PdfDocument.Open(path);
        var page = pdfDoc.GetPage(1);

        var lowDpi = PageImageRenderer.RenderPage(page, dpi: 72);
        var highDpi = PageImageRenderer.RenderPage(page, dpi: 300);

        // Higher DPI should produce a larger image
        Assert.True(highDpi.Length > lowDpi.Length,
            $"300 DPI image ({highDpi.Length} bytes) should be larger than 72 DPI image ({lowDpi.Length} bytes).");
    }

    [Fact]
    public void RenderRegion_ReturnsValidPng()
    {
        var path = IntegrationHelpers.GetDocumentPath("data");
        using var pdfDoc = PdfDocument.Open(path);
        var page = pdfDoc.GetPage(1);

        var region = new PdfRectangle(0, 0, page.Width, page.Height);
        var result = PageImageRenderer.RenderRegion(page, region);

        Assert.NotNull(result);
        Assert.True(result.Length > 4);
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
        Assert.Equal(0x4E, result[2]);
        Assert.Equal(0x47, result[3]);
    }

    [Fact]
    public void RenderRegion_SmallRegion_ProducesSmallerOutputThanFullPage()
    {
        var path = IntegrationHelpers.GetDocumentPath("data");
        using var pdfDoc = PdfDocument.Open(path);
        var page = pdfDoc.GetPage(1);

        var fullPage = PageImageRenderer.RenderPage(page, dpi: 150);
        var smallRegion = new PdfRectangle(0, 0, page.Width / 4, page.Height / 4);
        var regionImage = PageImageRenderer.RenderRegion(page, smallRegion, dpi: 150);

        Assert.True(regionImage.Length < fullPage.Length,
            $"Region image ({regionImage.Length} bytes) should be smaller than full page ({fullPage.Length} bytes).");
    }
}
#endif
