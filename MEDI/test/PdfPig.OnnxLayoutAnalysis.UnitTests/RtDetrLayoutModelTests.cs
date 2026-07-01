#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.Models;
using Xunit;

public class RtDetrLayoutModelTests
{
    #region Constructor

    [Fact]
    public void Constructor_StoresModelPath()
    {
        using var model = new RtDetrLayoutModel("test_model.onnx");
        Assert.Equal("test_model.onnx", model.ModelPath);
    }

    [Fact]
    public void Constructor_NullModelPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RtDetrLayoutModel(null!));
    }

    #endregion

    #region LabelMapping

    [Fact]
    public void LabelMapping_Has17Entries()
    {
        using var model = new RtDetrLayoutModel("test.onnx");
        Assert.Equal(17, model.LabelMapping.Count);
    }

    [Fact]
    public void LabelMapping_ContainsExpectedLabels()
    {
        using var model = new RtDetrLayoutModel("test.onnx");
        var mapping = model.LabelMapping;

        Assert.Equal("caption", mapping[0]);
        Assert.Equal("footnote", mapping[1]);
        Assert.Equal("formula", mapping[2]);
        Assert.Equal("list_item", mapping[3]);
        Assert.Equal("page_footer", mapping[4]);
        Assert.Equal("page_header", mapping[5]);
        Assert.Equal("picture", mapping[6]);
        Assert.Equal("section_header", mapping[7]);
        Assert.Equal("table", mapping[8]);
        Assert.Equal("text", mapping[9]);
        Assert.Equal("title", mapping[10]);
        Assert.Equal("document_index", mapping[11]);
        Assert.Equal("code", mapping[12]);
        Assert.Equal("checkbox_selected", mapping[13]);
        Assert.Equal("checkbox_unselected", mapping[14]);
        Assert.Equal("form", mapping[15]);
        Assert.Equal("key_value_region", mapping[16]);
    }

    [Fact]
    public void LabelMapping_ContiguousKeys_0To16()
    {
        using var model = new RtDetrLayoutModel("test.onnx");
        var mapping = model.LabelMapping;

        for (int i = 0; i <= 16; i++)
        {
            Assert.True(mapping.ContainsKey(i), $"Missing key {i}");
        }
    }

    [Fact]
    public void LabelMapping_AllValuesNonEmpty()
    {
        using var model = new RtDetrLayoutModel("test.onnx");

        foreach (var kvp in model.LabelMapping)
        {
            Assert.False(string.IsNullOrWhiteSpace(kvp.Value),
                $"Label for key {kvp.Key} should not be empty");
        }
    }

    #endregion

    #region ILayoutDetectionModel interface

    [Fact]
    public void ImplementsILayoutDetectionModel()
    {
        using var model = new RtDetrLayoutModel("test.onnx");
        Assert.IsAssignableFrom<ILayoutDetectionModel>(model);
    }

    [Fact]
    public void ImplementsIDisposable()
    {
        using var model = new RtDetrLayoutModel("test.onnx");
        Assert.IsAssignableFrom<IDisposable>(model);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var model = new RtDetrLayoutModel("test.onnx");
        model.Dispose();
        model.Dispose(); // Should not throw
    }

    #endregion

    #region Preprocess (requires model file)

    [SkippableFact]
    public void Preprocess_NullImage_Throws()
    {
        string modelPath = FindModelPath();
        Skip.IfNot(File.Exists(modelPath), "ONNX model file not found; skipping.");

        using var model = new RtDetrLayoutModel(modelPath);
        Assert.Throws<ArgumentNullException>(() => model.Preprocess(null!, 100, 100));
    }

    #endregion

    #region Postprocess (requires model file)

    [SkippableFact]
    public void Postprocess_NullResults_Throws()
    {
        string modelPath = FindModelPath();
        Skip.IfNot(File.Exists(modelPath), "ONNX model file not found; skipping.");

        using var model = new RtDetrLayoutModel(modelPath);
        Assert.Throws<ArgumentNullException>(() => model.Postprocess(null!, 640, 640));
    }

    #endregion

    #region Helpers

    private static string FindModelPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "rtdetr.onnx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rtdetr.onnx"),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    #endregion
}
#endif
