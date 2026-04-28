#if NET8_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Xunit;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

public class PdfPigReaderOptionsValidatorTests
{
    [Fact]
    public void DefaultOptions_Succeeds()
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
    public void RenderDpi_Zero_Fails()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader(o => o.RenderDpi = 0);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PdfPigReaderOptions>>();
        var ex = Assert.Throws<OptionsValidationException>(() => options.Value);

        Assert.Contains("RenderDpi", ex.Message);
    }

    [Fact]
    public void RenderDpi_Negative_Fails()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader(o => o.RenderDpi = -1);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PdfPigReaderOptions>>();
        var ex = Assert.Throws<OptionsValidationException>(() => options.Value);

        Assert.Contains("RenderDpi", ex.Message);
    }

    [Fact]
    public void RenderDpi_One_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddPdfPigReader(o => o.RenderDpi = 1);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PdfPigReaderOptions>>();
        var opts = options.Value;

        Assert.Equal(1, opts.RenderDpi);
    }

    [Fact]
    public void CustomValidOptions_Succeeds()
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
}
#endif
