#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using Xunit;

public class OnnxSegmenterOptionsValidatorTests
{
    #region Helpers

    private static IOptions<OnnxSegmenterOptions> BuildOptions(Action<OnnxSegmenterOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOnnxPageSegmenter<StubModel>(configure);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OnnxSegmenterOptions>>();
    }

    private sealed class StubModel : ILayoutDetectionModel
    {
        public string ModelPath => "stub.onnx";

        public IReadOnlyDictionary<int, string> LabelMapping { get; } = new Dictionary<int, string>();

        public IReadOnlyList<NamedOnnxValue> Preprocess(SKBitmap p, int w, int h)
        {
            return Array.Empty<NamedOnnxValue>();
        }

        public IReadOnlyList<LayoutDetection> Postprocess(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> r, int w, int h)
        {
            return Array.Empty<LayoutDetection>();
        }

        public void Dispose() { }
    }

    #endregion

    #region ConfidenceThreshold

    [Fact]
    public void DefaultOptions_Succeeds()
    {
        var options = BuildOptions();

        var opts = options.Value;

        Assert.Equal(0.3f, opts.ConfidenceThreshold, 4);
        Assert.Equal(150, opts.RenderDpi);
    }

    [Fact]
    public void ConfidenceThreshold_Negative_Fails()
    {
        var options = BuildOptions(o => o.ConfidenceThreshold = -0.1f);

        var ex = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains("ConfidenceThreshold", ex.Message);
    }

    [Fact]
    public void ConfidenceThreshold_AboveOne_Fails()
    {
        var options = BuildOptions(o => o.ConfidenceThreshold = 1.1f);

        var ex = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains("ConfidenceThreshold", ex.Message);
    }

    [Fact]
    public void ConfidenceThreshold_ExactlyZero_Succeeds()
    {
        var options = BuildOptions(o => o.ConfidenceThreshold = 0f);

        var opts = options.Value;

        Assert.Equal(0f, opts.ConfidenceThreshold, 4);
    }

    [Fact]
    public void ConfidenceThreshold_ExactlyOne_Succeeds()
    {
        var options = BuildOptions(o => o.ConfidenceThreshold = 1f);

        var opts = options.Value;

        Assert.Equal(1f, opts.ConfidenceThreshold, 4);
    }

    #endregion

    #region RenderDpi

    [Fact]
    public void RenderDpi_Zero_Fails()
    {
        var options = BuildOptions(o => o.RenderDpi = 0);

        var ex = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains("RenderDpi", ex.Message);
    }

    [Fact]
    public void RenderDpi_Negative_Fails()
    {
        var options = BuildOptions(o => o.RenderDpi = -1);

        var ex = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains("RenderDpi", ex.Message);
    }

    [Fact]
    public void RenderDpi_One_Succeeds()
    {
        var options = BuildOptions(o => o.RenderDpi = 1);

        var opts = options.Value;

        Assert.Equal(1, opts.RenderDpi);
    }

    #endregion
}
#endif
