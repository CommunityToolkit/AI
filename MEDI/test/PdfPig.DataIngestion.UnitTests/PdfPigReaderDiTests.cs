#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Xunit;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

public class PdfPigReaderDiTests
{
    [Fact]
    public void AddPdfPigReader_RegistersPdfPigReader()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader();

        Assert.Contains(services, d => d.ServiceType == typeof(PdfPigReader));
    }

    [Fact]
    public void AddPdfPigReader_ConfigureAppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader(o =>
        {
            o.Mode = PdfReadingMode.Hybrid;
            o.RenderDpi = 300;
        });
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PdfPigReaderOptions>>();
        var opts = options.Value;

        Assert.Equal(PdfReadingMode.Hybrid, opts.Mode);
        Assert.Equal(300, opts.RenderDpi);
    }

    [Fact]
    public void AddPdfPigReader_DefaultOptionsWithoutConfigure()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader();
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PdfPigReaderOptions>>();
        var opts = options.Value;

        Assert.Equal(PdfReadingMode.TextOnly, opts.Mode);
        Assert.Equal(150, opts.RenderDpi);
    }

    [Fact]
    public void AddPdfPigReader_InvalidDpi_ThrowsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader(o => o.RenderDpi = 0);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PdfPigReaderOptions>>();
        var ex = Assert.Throws<OptionsValidationException>(() => options.Value);

        Assert.Contains("RenderDpi", ex.Message);
    }

    [Fact]
    public void IOptionsCtor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PdfPigReader((IOptions<PdfPigReaderOptions>)null!));
    }

    [Fact]
    public async Task IOptionsCtor_UsesOptionsValues()
    {
        var options = Options.Create(new PdfPigReaderOptions
        {
            Mode = PdfReadingMode.Hybrid,
            RenderDpi = 72
        });
        var reader = new PdfPigReader(options);
        var pdfBytes = CreateBlankPagePdf();

        using var stream = new MemoryStream(pdfBytes);
        var doc = await reader.ReadAsync(stream, "blank.pdf", "application/pdf");

        Assert.Single(doc.Sections);
        var section = doc.Sections[0];
        Assert.True(section.Metadata.ContainsKey("page_image"),
            "Hybrid mode should render page images.");
    }

    [Fact]
    public void AddPdfPigReader_IdempotentRegistration()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader();
        services.AddPdfPigReader();

        var readerDescriptors = services
            .Where(d => d.ServiceType == typeof(PdfPigReader))
            .ToList();

        Assert.Single(readerDescriptors);
    }

    private static byte[] CreateBlankPagePdf()
    {
        using var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }
}
#endif
