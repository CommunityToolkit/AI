// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class BuilderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"onnx-recipe-{Guid.NewGuid():N}");
    private readonly string _layoutPath;

    public BuilderTests()
    {
        Directory.CreateDirectory(_directory);
        _layoutPath = Path.Combine(_directory, "synthetic-layout.onnx");
        File.WriteAllBytes(_layoutPath, LayoutCoordinateTests.CreateGraph());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void OrdinaryConstructionAndBuildRequireLayoutPath(string? path)
    {
        var options = new OnnxLayoutOptions { LayoutModelPath = path };
        var builder = new PdfPigOnnxDocumentExtractionClientBuilder(options);
        Assert.ThrowsAny<ArgumentException>(() => new PdfPigOnnxDocumentExtractionClient(options));
        Assert.ThrowsAny<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void InvalidGroupsAreNotDisabledOrLoadedDuringCallbacks()
    {
        var builder = new PdfPigOnnxDocumentExtractionClientBuilder(new() { LayoutModelPath = _layoutPath });
        Assert.Same(builder, builder.WithOcr(ocr => ocr.Mode = (OnnxOcrMode)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
        builder.WithOcr(ocr => ocr.Mode = OnnxOcrMode.PagesWithoutNativeText);
        Assert.Throws<ArgumentNullException>(() => builder.Build());

        var tableBuilder = new PdfPigOnnxDocumentExtractionClientBuilder(new() { LayoutModelPath = _layoutPath });
        tableBuilder.WithTableStructure(_ => { });
        Assert.Throws<ArgumentNullException>(() => tableBuilder.Build());
        Assert.Throws<ArgumentNullException>(() => builder.WithOcr(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithTableStructure(null!));
        Assert.Throws<ArgumentNullException>(() => new PdfPigOnnxDocumentExtractionClientBuilder(null!));
        Assert.Throws<ArgumentNullException>(() => new PdfPigOnnxDocumentExtractionClient((OnnxLayoutOptions)null!));
    }

    [Fact]
    public void NullGroupsDisableAndNonNullGroupsLoadEagerly()
    {
        using var native = new PdfPigOnnxDocumentExtractionClient(new OnnxLayoutOptions { LayoutModelPath = _layoutPath });
        Assert.NotNull(native.GetService(typeof(InferenceSession)));
        string missingDetector = Path.Combine(_directory, "detector.onnx");
        var options = new OnnxLayoutOptions
        {
            LayoutModelPath = _layoutPath,
            Ocr = new() { DetectorModelPath = missingDetector, RecognizerModelPath = "recognizer.onnx" },
        };
        var builder = new PdfPigOnnxDocumentExtractionClientBuilder(options);
        Assert.Equal(missingDetector, Assert.Throws<FileNotFoundException>(() => builder.Build()).FileName);
        Assert.Equal(missingDetector, Assert.Throws<FileNotFoundException>(() => new PdfPigOnnxDocumentExtractionClient(options)).FileName);
        options.Ocr = null;
        options.TableStructure = new() { ModelPath = Path.Combine(_directory, "tables.onnx") };
        Assert.Equal(options.TableStructure.ModelPath,
            Assert.Throws<FileNotFoundException>(() => new PdfPigOnnxDocumentExtractionClient(options)).FileName);
    }

    [Fact]
    public void BuilderCapturesNestedOptionsAndCallbackResultsWithoutDeferringCallbacks()
    {
        string detector = Path.Combine(_directory, "captured-detector.onnx");
        var options = new OnnxLayoutOptions
        {
            LayoutModelPath = _layoutPath,
            Ocr = new() { DetectorModelPath = detector, RecognizerModelPath = "recognizer.onnx" },
            TableStructure = new() { ModelPath = "table.onnx" },
        };
        var builder = new PdfPigOnnxDocumentExtractionClientBuilder(options);
        options.LayoutModelPath = "caller-mutated-layout";
        options.Ocr.DetectorModelPath = "caller-mutated-detector";
        options.TableStructure.ModelPath = null;
        OnnxOcrOptions? escaped = null;
        OnnxTableStructureOptions? escapedTable = null;
        int calls = 0;
        builder.WithOcr(ocr => { calls++; escaped = ocr; })
            .WithTableStructure(table => { calls++; escapedTable = table; });
        Assert.Equal(2, calls);
        escaped!.DetectorModelPath = "escaped-mutation";
        escapedTable!.ModelPath = null;
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(detector, Assert.Throws<FileNotFoundException>(() => builder.Build()).FileName);
        }

        Assert.Equal(2, calls);
        builder.WithOcr(ocr => ocr.DetectorModelPath = Path.Combine(_directory, "new-recipe.onnx"));
        Assert.EndsWith("new-recipe.onnx", Assert.Throws<FileNotFoundException>(() => builder.Build()).FileName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationOrderDoesNotChangeFixedLoadingDependencies(bool tablesFirst)
    {
        var builder = new PdfPigOnnxDocumentExtractionClientBuilder(new() { LayoutModelPath = _layoutPath });
        Action<OnnxOcrOptions> ocr = options =>
        {
            options.DetectorModelPath = Path.Combine(_directory, "first-detector.onnx");
            options.RecognizerModelPath = "recognizer.onnx";
        };
        Action<OnnxTableStructureOptions> table = options => options.ModelPath = Path.Combine(_directory, "later-table.onnx");
        if (tablesFirst)
        {
            builder.WithTableStructure(table).WithColumnReadingOrder().WithOcr(ocr);
        }
        else
        {
            builder.WithOcr(ocr).WithColumnReadingOrder().WithTableStructure(table);
        }

        Assert.EndsWith("first-detector.onnx", Assert.Throws<FileNotFoundException>(() => builder.Build()).FileName);
    }

    [Fact]
    public async Task BuilderAndDirectConstructionConvergeAndRepeatedBuildsDoNotMutateClients()
    {
        var options = new OnnxLayoutOptions
        {
            LayoutModelPath = _layoutPath,
            UseCorrectedLayoutCoordinates = true,
            UseColumnReadingOrder = true,
        };
        var builder = new PdfPigOnnxDocumentExtractionClientBuilder(options).WithColumnReadingOrder();
        using var direct = new PdfPigOnnxDocumentExtractionClient(options);
        using var first = builder.Build();
        options.MaximumPagePixels = 1;
        options.UseCorrectedLayoutCoordinates = false;
        options.Ocr = new();
        builder.WithOcr(ocr =>
        {
            ocr.DetectorModelPath = Path.Combine(_directory, "missing.onnx");
            ocr.RecognizerModelPath = "recognizer.onnx";
        });
        Assert.Throws<FileNotFoundException>(() => builder.Build());
        using var stream1 = new MemoryStream(LayoutPdfFixture.Create());
        using var stream2 = new MemoryStream(LayoutPdfFixture.Create());
        var expected = await direct.ExtractAsync(stream1, "same", "application/pdf");
        var actual = await first.ExtractAsync(stream2, "same", "application/pdf");
        Assert.Equal(
            expected.EnumerateContent().Select(element => (element.Content as TextContent)?.Text),
            actual.EnumerateContent().Select(element => (element.Content as TextContent)?.Text));
        var expectedRegions = Assert.IsAssignableFrom<IReadOnlyList<PdfPigLayoutRegion>>(expected.RawRepresentation);
        var actualRegions = Assert.IsAssignableFrom<IReadOnlyList<PdfPigLayoutRegion>>(actual.RawRepresentation);
        Assert.Equal(300, actualRegions.Count);
        Assert.Equal(expectedRegions.Select(region => region.Bounds), actualRegions.Select(region => region.Bounds));
        using var second = new PdfPigOnnxDocumentExtractionClientBuilder(new() { LayoutModelPath = _layoutPath }).Build();
        Assert.NotSame(first.GetService(typeof(InferenceSession)), second.GetService(typeof(InferenceSession)));
        second.Dispose();
        using var stream3 = new MemoryStream(LayoutPdfFixture.Create());
        Assert.Single((await first.ExtractAsync(stream3, "alive", "application/pdf")).SourcePages);
    }

    [Fact]
    public void FailedConstructionAndDisposalNeverTakeBorrowedSessionOwnership()
    {
        using var session = new InferenceSession(LayoutCoordinateTests.CreateGraph());
        Assert.Throws<ArgumentException>(() => new PdfPigOnnxDocumentExtractionClient(session,
            new() { LayoutModelPath = _layoutPath }));
        Assert.Throws<ArgumentException>(() => new PdfPigOnnxDocumentExtractionClient(session,
            new() { LayoutModelPath = "" }));
        Assert.Throws<FileNotFoundException>(() => new PdfPigOnnxDocumentExtractionClient(session,
            new() { TableStructure = new() { ModelPath = Path.Combine(_directory, "missing-table.onnx") } }));
        using (var client = new PdfPigOnnxDocumentExtractionClient(session))
        {
            Assert.Same(session, client.GetService(typeof(InferenceSession)));
        }

        using var results = session.Run([
            NamedOnnxValue.CreateFromTensor("images", new DenseTensor<byte>([1, 3, 640, 640])),
            NamedOnnxValue.CreateFromTensor("orig_target_sizes", new DenseTensor<long>(new long[] { 400, 550 }, [1, 2])),
        ]);
        Assert.Equal(300, results.Single(value => value.Name == "labels").AsTensor<long>().Length);
    }

    [Fact]
    public void OwnedGraphValidationFailurePreservesBorrowedRuntimeOptions()
    {
        string wrongGraph = Path.Combine(_directory, "wrong-contract.onnx");
        File.WriteAllBytes(wrongGraph, Convert.FromBase64String(
            "CAg6VAoZCgVpbnB1dBIGb3V0cHV0IghJZGVudGl0eRIMaW5jb21wYXRpYmxlWhMKBWlucHV0EgoKCAgBEgQKAggBYhQKBm91dHB1dBIKCggIARIECgIIAUIECgAQEg=="));
        using var runtime = new SessionOptions { IntraOpNumThreads = 2 };
        var options = new OnnxLayoutOptions { LayoutModelPath = wrongGraph, SessionOptions = runtime };
        Assert.Throws<ArgumentException>(() => new PdfPigOnnxDocumentExtractionClient(options));
        Assert.Throws<ArgumentException>(() => new PdfPigOnnxDocumentExtractionClientBuilder(options).Build());
        options.LayoutModelPath = _layoutPath;
        using (var client = new PdfPigOnnxDocumentExtractionClient(options))
        {
            Assert.NotNull(client.GetService(typeof(InferenceSession)));
        }

        Assert.Equal(2, runtime.IntraOpNumThreads);
        runtime.IntraOpNumThreads = 1;
        using var next = new PdfPigOnnxDocumentExtractionClientBuilder(options).Build();
        Assert.NotNull(next.GetService(typeof(InferenceSession)));
    }

    [Fact]
    public void ThrowingCallbacksDoNotPartiallyMutateTheRecipe()
    {
        var builder = new PdfPigOnnxDocumentExtractionClientBuilder(new() { LayoutModelPath = _layoutPath });
        Assert.Throws<InvalidOperationException>(() => builder.WithOcr(ocr =>
        {
            ocr.Mode = (OnnxOcrMode)123;
            throw new InvalidOperationException("callback failure");
        }));
        Assert.Throws<InvalidOperationException>(() => builder.WithTableStructure(table =>
        {
            table.ModelPath = "missing.onnx";
            throw new InvalidOperationException("callback failure");
        }));
        using var client = builder.Build();
        Assert.NotNull(client.GetService(typeof(InferenceSession)));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
