#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using System;
using System.Collections.Generic;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.Models;
using Xunit;

public class ConfigurableLayoutModelTests
{
    #region Constructor guard clauses

    [Fact]
    public void Constructor_NullModelPath_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ConfigurableLayoutModel(null!, new LayoutModelOptions()));

        Assert.Equal("modelPath", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ConfigurableLayoutModel("model.onnx", null!));

        Assert.Equal("options", ex.ParamName);
    }

    #endregion

    #region Properties

    [Fact]
    public void ModelPath_ReturnsConstructorValue()
    {
        var model = new ConfigurableLayoutModel("path/to/model.onnx", new LayoutModelOptions());

        Assert.Equal("path/to/model.onnx", model.ModelPath);
    }

    [Fact]
    public void LabelMapping_WithClassLabels_ReturnsSameLabels()
    {
        var labels = new Dictionary<int, string> { [0] = "text", [1] = "table" };
        var options = new LayoutModelOptions { ClassLabels = labels };
        var model = new ConfigurableLayoutModel("model.onnx", options);

        var mapping = model.LabelMapping;

        Assert.Equal(2, mapping.Count);
        Assert.Equal("text", mapping[0]);
        Assert.Equal("table", mapping[1]);
    }

    [Fact]
    public void LabelMapping_WithoutClassLabels_ReturnsEmptyDictionary()
    {
        var model = new ConfigurableLayoutModel("model.onnx", new LayoutModelOptions());

        var mapping = model.LabelMapping;

        Assert.NotNull(mapping);
        Assert.Empty(mapping);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var model = new ConfigurableLayoutModel("model.onnx", new LayoutModelOptions());

        model.Dispose();
        model.Dispose();
    }

    #endregion
}
#endif
