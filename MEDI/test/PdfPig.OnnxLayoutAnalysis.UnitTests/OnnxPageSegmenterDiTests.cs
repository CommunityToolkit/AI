#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using Xunit;

public class OnnxPageSegmenterDiTests
{
    #region Helpers

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

    #region Service registration

    [Fact]
    public void AddOnnxPageSegmenter_RegistersILayoutDetectionModel()
    {
        var services = new ServiceCollection();
        services.AddOnnxPageSegmenter<StubModel>();
        var provider = services.BuildServiceProvider();

        var model = provider.GetService<ILayoutDetectionModel>();

        Assert.NotNull(model);
        Assert.IsType<StubModel>(model);
    }

    [Fact]
    public void AddOnnxPageSegmenter_RegistersIPageSegmenter()
    {
        var services = new ServiceCollection();
        services.AddOnnxPageSegmenter<StubModel>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPageSegmenter));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddOnnxPageSegmenter_IdempotentRegistration()
    {
        var services = new ServiceCollection();
        services.AddOnnxPageSegmenter<StubModel>();
        services.AddOnnxPageSegmenter<StubModel>();

        int modelCount = services.Count(d => d.ServiceType == typeof(ILayoutDetectionModel));
        int segmenterCount = services.Count(d => d.ServiceType == typeof(OnnxPageSegmenter));
        int pageSegmenterCount = services.Count(d => d.ServiceType == typeof(IPageSegmenter));

        Assert.Equal(1, modelCount);
        Assert.Equal(1, segmenterCount);
        Assert.Equal(1, pageSegmenterCount);
    }

    #endregion

    #region Options configuration

    [Fact]
    public void AddOnnxPageSegmenter_ConfigureAppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddOnnxPageSegmenter<StubModel>(o =>
        {
            o.ConfidenceThreshold = 0.8f;
            o.RenderDpi = 300;
        });
        var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<IOptions<OnnxSegmenterOptions>>().Value;

        Assert.Equal(0.8f, opts.ConfidenceThreshold, 4);
        Assert.Equal(300, opts.RenderDpi);
    }

    [Fact]
    public void AddOnnxPageSegmenter_DefaultOptionsWithoutConfigure()
    {
        var services = new ServiceCollection();
        services.AddOnnxPageSegmenter<StubModel>();
        var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<IOptions<OnnxSegmenterOptions>>().Value;

        Assert.Equal(0.3f, opts.ConfidenceThreshold, 4);
        Assert.Equal(150, opts.RenderDpi);
        Assert.Null(opts.SessionOptions);
    }

    [Fact]
    public void AddOnnxPageSegmenter_InvalidOptions_ThrowsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddOnnxPageSegmenter<StubModel>(o => o.ConfidenceThreshold = -1f);
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OnnxSegmenterOptions>>();

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    #endregion

    #region Constructor null checks

    [Fact]
    public void IOptionsCtor_NullOptions_Throws()
    {
        var model = new StubModel();

        Assert.Throws<ArgumentNullException>(
            () => new OnnxPageSegmenter(model, (IOptions<OnnxSegmenterOptions>)null!));
    }

    #endregion
}
#endif
