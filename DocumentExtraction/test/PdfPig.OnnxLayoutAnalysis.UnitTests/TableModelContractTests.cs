// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.OnnxRuntime.Tensors;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class TableModelContractTests
{
    [Fact]
    public void TableCapabilityIsIndependentAndCaptured()
    {
        var options = new OnnxLayoutOptions { TableStructure = new() { ModelPath = "table.onnx" } };
        var snapshot = options.Snapshot();
        options.TableStructure.ModelPath = null;
        options.TableStructure = null;
        Assert.Equal("table.onnx", snapshot.TableStructure!.ModelPath);
        Assert.Null(snapshot.Ocr);
        Assert.Null(new OnnxLayoutOptions().Snapshot().TableStructure);
        Assert.Throws<ArgumentNullException>(() => new OnnxLayoutOptions { TableStructure = new() }.Snapshot());
    }

    [Fact]
    public void SingletonRgbImageNetResizeUsesActualCropAndNoPixelMaskOrPadding()
    {
        var bgra = new byte[16 * 8 * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 255;
            bgra[i + 3] = 255;
        }

        var raster = new PageRaster(bgra, 16, 8, 32, 16);
        var crop = TableStructureModel.Prepare(raster, new(8, 4, 24, 12), default);
        Assert.Equal((4, 2, 8, 4), (crop.Left, crop.Top, crop.Width, crop.Height));
        Assert.Equal(new[] { 1, 3, 500, 1000 }, crop.Input.Dimensions.ToArray());
        Assert.Equal((float)(-.485 / .229), crop.Input[0, 0, 0, 0], .000001f);
        Assert.Equal((float)(-.456 / .224), crop.Input[0, 1, 200, 200], .000001f);
        Assert.Equal((float)((1 - .406) / .225), crop.Input[0, 2, 499, 999], .000001f);
        Assert.All(crop.Input, value => Assert.NotEqual(0, value));
        Array.Clear(bgra);
        var transparent = TableStructureModel.Prepare(raster, new(0, 0, 32, 16), default);
        Assert.Equal((float)((1 - .485) / .229), transparent.Input[0, 0, 0, 0], .000001f);
    }

    [Fact]
    public void InvalidTableRasterCropAndCancellationFailExplicitly()
    {
        Assert.Throws<InvalidDataException>(() => TableStructureModel.Prepare(new([], 4, 4, 4, 4), new(0, 0, 4, 4), default));
        Assert.Throws<InvalidDataException>(() => TableStructureModel.Prepare(new(new byte[64], 4, 4, double.NaN, 4), new(0, 0, 4, 4), default));
        Assert.Throws<InvalidDataException>(() => TableStructureModel.Prepare(new(new byte[64], 4, 4, 4, 4), new(5, 5, 7, 7), default));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() =>
            TableStructureModel.Prepare(new([], 0, 0, 0, 0), new(0, 0, 1, 1), cancel.Token));
        Assert.Equal(cancel.Token, error.CancellationToken);
    }

    [Fact]
    public void TatrSoftmaxIncludesNoObjectAndPreservesEveryOriginalQuery()
    {
        var (logits, boxes) = Outputs();
        logits[0, 0, 2] = 9;
        var raster = new PageRaster([], 200, 100, 400, 200);
        var crop = new TableCrop(new DenseTensor<float>([1, 3, 1, 1]), 20, 10, 100, 50);
        var noObject = TableStructureModel.Decode(logits, boxes, raster, crop, default);
        Assert.Empty(noObject.Accepted);
        Assert.Equal(125, noObject.Proposals.Count);
        Assert.Equal("no object", noObject.Proposals[0].Label);
        Assert.Equal(7, noObject.Proposals[0].Logits.Count);
        logits[0, 0, 6] = -10;
        var row = TableStructureModel.Decode(logits, boxes, raster, crop, default);
        var accepted = Assert.Single(row.Accepted);
        Assert.Equal(2, accepted.Label);
        Assert.Equal(new PdfRectangle(120, 120, 160, 140), accepted.Bounds);
        Assert.InRange(accepted.Confidence, .99f, 1);
        logits[0, 0, 2] = 0;
        for (int c = 0; c < 7; c++) logits[0, 0, c] = float.MaxValue;
        var uniform = TableStructureModel.Decode(logits, boxes, raster, crop, default);
        Assert.Empty(uniform.Accepted);
        Assert.Equal(1f / 7, uniform.Proposals[0].Confidence, .000001f);
        boxes[0, 0, 0] = 4;
        Assert.Null(TableStructureModel.Decode(logits, boxes, raster, crop, default).Proposals[0].Bounds);
        Assert.Equal(.5f, row.Proposals[0].NormalizedBox[0]);
        Assert.Equal(9, row.Proposals[0].Logits[2]);
        Assert.Throws<NotSupportedException>(() => ((IList<float>)row.Proposals[0].Logits).Clear());
    }

    [Fact]
    public void MalformedTatrEvenNoObjectReportsActualQueryAndNeverFallsBack()
    {
        var (logits, boxes) = Outputs();
        var raster = new PageRaster([], 20, 10, 20, 10);
        var crop = new TableCrop(new DenseTensor<float>([1, 3, 1, 1]), 0, 0, 20, 10);
        logits[0, 17, 3] = float.NaN;
        Assert.Contains("query 17", Assert.Throws<InvalidDataException>(() => TableStructureModel.Decode(logits, boxes, raster, crop, default)).Message);
        logits[0, 17, 3] = 0;
        boxes[0, 24, 2] = 0;
        Assert.Contains("query 24", Assert.Throws<InvalidDataException>(() => TableStructureModel.Decode(logits, boxes, raster, crop, default)).Message);
        Assert.Throws<InvalidDataException>(() => TableStructureModel.Decode(new DenseTensor<float>([1, 126, 7]), boxes, raster, crop, default));
        Assert.Throws<OperationCanceledException>(() => TableStructureModel.Decode(logits, boxes, raster, crop, new(true)));
    }

    [Fact]
    public void StructuredCellsPreserveZeroUnknownHeadersAndUnassignedNativeTextWithoutAncestorDuplication()
    {
        using var pdf = PdfPageRenderer.Open(LayoutPdfFixture.Create());
        var page = pdf.GetPage(1);
        var document = new ExtractionDocument("tables");
        var regions = LayoutAssembler.AddPage(document, page, [new("table", .9f, new(0, 300, 400, 420))], default);
        var region = Assert.Single(regions);
        var word = region.Words[0];
        RasterPoint[] zeroQuad = [new(20, 200), new(40, 200), new(40, 220), new(20, 220)];
        var zero = new PdfPigOcrRegion(1, new(new byte[400 * 550 * 4], 400, 550, 400, 550),
            new(zeroQuad, .9f), new([], 20, 20, new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, zeroQuad), "0", .9f);
        var token = new TableTextToken(zero.Text, zero.Bounds, zero);
        var extra = new TableTextToken(word.Text, LayoutAssembler.AxisAligned(word.BoundingBox), word);
        var grid = new TableGrid(2, 2,
        [
            new(0, 0, 1, 2, "columnHeader", new(0, 360, 400, 420), [token]),
            new(1, 0, 1, 1, null, new(0, 300, 200, 360), []),
            new(1, 1, 1, 1, null, new(200, 300, 400, 360), []),
        ], [extra]);
        var observation = new PdfPigTableStructure(region, [], grid);
        Assert.True(observation.HasStructure);
        Assert.Equal("0", observation.Cells[0].Text);
        Assert.Equal(PdfPigTableTextStatus.Recognized, observation.Cells[0].TextStatus);
        Assert.Null(observation.Cells[1].Text);
        Assert.Equal(PdfPigTableTextStatus.Unobserved, observation.Cells[1].TextStatus);
        TablePageAssembler.Replace(document.Sections[0], document.SourcePages[0], observation, default);
        var table = Assert.Single(document.Sections[0].Elements.OfType<ExtractionDocumentTableElement>());
        Assert.Null(table.Content);
        Assert.Equal((2, 2), (table.Cells.RowCount, table.Cells.ColumnCount));
        Assert.Equal(2, table.Cells[0, 0]!.ColumnSpan);
        Assert.Equal("columnHeader", table.Cells[0, 0]!.Role);
        Assert.Null(table.Cells[1, 0]!.Content);
        var recognized = Assert.IsType<ExtractionDocumentTextElement>(table.Cells[0, 0]!.Content);
        Assert.Equal("0", Assert.IsType<TextContent>(recognized.Content).Text);
        Assert.Same(zero, Assert.Single(recognized.Sources).GetRawRepresentation());
        Assert.Equal(word.Text, Assert.IsType<TextContent>(Assert.Single(
            document.Sections[0].Elements.OfType<ExtractionDocumentTextElement>(),
            element => element.Sources.Any(source => ReferenceEquals(source.GetRawRepresentation(), word))).Content).Text);
        Assert.Contains(document.Sections[0].Elements.OfType<ExtractionDocumentTextElement>(),
            element => element.Content is TextContent text && text.Text == "Unmatched text is retained.");
        Assert.Same(observation, Assert.Single(table.Sources).GetRawRepresentation());
        Assert.Throws<NotSupportedException>(() => ((IList<PdfPigTableCellObservation>)observation.Cells).Clear());
    }

    [Fact]
    public void NoStructureIsExplicitOriginalZeroGridNotInventedCellsOrDuplicateText()
    {
        using var pdf = PdfPageRenderer.Open(LayoutPdfFixture.Create());
        var document = new ExtractionDocument("unknown-table");
        var region = Assert.Single(LayoutAssembler.AddPage(document, pdf.GetPage(1), [new("table", .9f, new(0, 300, 400, 420))], default));
        var original = Assert.Single(document.Sections[0].Elements.OfType<ExtractionDocumentTableElement>());
        var token = new TableTextToken(region.Words[0].Text, LayoutAssembler.AxisAligned(region.Words[0].BoundingBox), region.Words[0]);
        var observation = new PdfPigTableStructure(region, [], new(0, 0, [], [token]));
        TablePageAssembler.Replace(document.Sections[0], document.SourcePages[0], observation, default);
        Assert.False(observation.HasStructure);
        Assert.Same(original, Assert.Single(document.Sections[0].Elements.OfType<ExtractionDocumentTableElement>()));
        Assert.Equal((0, 0), (original.Cells.RowCount, original.Cells.ColumnCount));
        Assert.Equal(2, original.Sources.Count);
        Assert.Same(observation, original.Sources[1].GetRawRepresentation());
        Assert.Equal(region.Words[0].Text, Assert.Single(observation.UnassignedText));
    }

    [Fact]
    public void ActualEmptyRecognitionIsDistinctFromUnobservedOrLiteralZero()
    {
        var point = new RasterPoint[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var empty = new PdfPigOcrRegion(1, new(new byte[400], 10, 10, 10, 10), new(point, .8f),
            new([], 10, 10, new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, point), "", 0);
        var cell = new PdfPigTableCellObservation(new(0, 0, 1, 1, null, new(0, 0, 10, 10), [new("", empty.Bounds, empty)]));
        Assert.Equal("", cell.Text);
        Assert.Equal(PdfPigTableTextStatus.NoRecognizedText, cell.TextStatus);
        Assert.Same(empty, Assert.Single(cell.Observations));
    }

    [Fact]
    public void ResponseGridBudgetAcceptsExactly65536BeforeRejectingFurtherAllocation()
    {
        var region = new PdfPigLayoutRegion(9, "table", .9f, new(0, 0, 1, 1), Array.Empty<Word>());
        var observation = new PdfPigTableStructure(region, [], new(128, 128, [], []));
        int remaining = 65536;
        for (int i = 0; i < 4; i++) remaining = TablePageAssembler.ReserveGrid(observation, remaining);
        Assert.Equal(0, remaining);
        var error = Assert.Throws<InvalidDataException>(() => TablePageAssembler.ReserveGrid(observation, remaining));
        Assert.Contains("Page 9", error.Message);
        Assert.Contains("65536", error.Message);
    }

    private static (DenseTensor<float> Logits, DenseTensor<float> Boxes) Outputs()
    {
        var logits = new DenseTensor<float>([1, 125, 7]);
        var boxes = new DenseTensor<float>([1, 125, 4]);
        for (int query = 0; query < 125; query++)
        {
            for (int label = 0; label < 7; label++) logits[0, query, label] = label == 6 ? 10 : -10;
            boxes[0, query, 0] = boxes[0, query, 1] = .5f;
            boxes[0, query, 2] = boxes[0, query, 3] = .2f;
        }

        return (logits, boxes);
    }
}
