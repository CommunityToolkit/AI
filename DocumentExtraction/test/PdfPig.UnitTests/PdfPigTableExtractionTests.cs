using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;

namespace CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;

public sealed class PdfPigTableExtractionTests
{
    private static readonly (int, int, int, int, string)[] PriceCells =
    [
        (0, 0, 1, 1, "Name"), (0, 1, 1, 1, "Qty"), (0, 2, 1, 1, "Price"),
        (1, 0, 1, 1, "Apple"), (1, 1, 1, 1, "3"), (1, 2, 1, 1, "1.50"),
        (2, 0, 1, 1, "Pear"), (2, 1, 1, 1, "5"), (2, 2, 1, 1, "2.25"),
    ];

    [Fact]
    public async Task ExtractAsync_RuledGrid_ReturnsTableAndKeepsEachWordOnce()
    {
        byte[] pdf = PdfFixture.CreatePage(
        [
            PdfFixture.Text(72, 740, "Report title"),
            .. PdfFixture.Grid(72, 700, 100, 20, PriceCells),
            PdfFixture.Text(72, 600, "Footer note"),
        ]);

        ExtractionDocument result = await ExtractAsync(pdf, new());

        var table = Assert.Single(result.Sections[0].Elements.OfType<ExtractionDocumentTableElement>());
        Assert.Equal(3, table.Cells.RowCount);
        Assert.Equal(3, table.Cells.ColumnCount);
        Assert.Equal(new[] { "Name", "Qty", "Price", "Apple", "3", "1.50", "Pear", "5", "2.25" },
            Enumerable.Range(0, 9).Select(index => CellText(table.Cells[index / 3, index % 3])));
        Assert.All(Enumerable.Range(0, 9).Select(index => table.Cells[index / 3, index % 3]!), cell =>
        {
            Assert.Equal(1, cell.RowSpan);
            Assert.Equal(1, cell.ColumnSpan);
            Assert.True(Inside(Assert.Single(cell.Sources).Polygon, Assert.Single(table.Sources).Polygon));
        });
        Assert.IsAssignableFrom<Tabula.Table>(Assert.Single(table.Sources).GetRawRepresentation());
        string text = string.Join(" ", result.Sections[0].Elements.OfType<ExtractionDocumentTextElement>().Select(Text));
        Assert.Contains("Report title", text, StringComparison.Ordinal);
        Assert.Contains("Footer note", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Apple", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Price", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_MergedHeaderCells_ReturnsSpans()
    {
        byte[] pdf = PdfFixture.CreatePage(PdfFixture.Grid(72, 700, 100, 20,
            (0, 0, 1, 2, "Fruit"), (0, 2, 2, 1, "Price"),
            (1, 0, 1, 1, "Name"), (1, 1, 1, 1, "Qty"),
            (2, 0, 1, 1, "Apple"), (2, 1, 1, 1, "3"), (2, 2, 1, 1, "1.50")));

        ExtractionDocument result = await ExtractAsync(pdf, new());

        var table = Assert.Single(result.Sections[0].Elements.OfType<ExtractionDocumentTableElement>());
        Assert.Equal((3, 3), (table.Cells.RowCount, table.Cells.ColumnCount));
        var fruit = table.Cells[0, 0]!;
        Assert.Equal(("Fruit", 1, 2), (CellText(fruit), fruit.RowSpan, fruit.ColumnSpan));
        var price = table.Cells[0, 2]!;
        Assert.Equal(("Price", 2, 1), (CellText(price), price.RowSpan, price.ColumnSpan));
        Assert.Null(table.Cells[0, 1]);
        Assert.Null(table.Cells[1, 2]);
        Assert.Equal("1.50", CellText(table.Cells[2, 2]));
    }

    [Fact]
    public async Task ExtractAsync_MultiBlockSegmenter_EmitsTablesAndTextTopToBottom()
    {
        byte[] pdf = PdfFixture.CreatePage(
        [
            PdfFixture.Text(72, 740, "Report title"),
            .. PdfFixture.Grid(72, 700, 100, 20, PriceCells),
            PdfFixture.Text(72, 600, "Footer note"),
            .. PdfFixture.Grid(72, 560, 100, 20, (0, 0, 1, 1, "Total"), (0, 1, 1, 1, "8"), (1, 0, 1, 1, "Tax"), (1, 1, 1, 1, "0")),
        ]);

        ExtractionDocument result = await ExtractAsync(pdf, new() { PageSegmenter = DocstrumBoundingBoxes.Instance });

        Assert.Equal(new[] { "Report title", "table:Name", "Footer note", "table:Total" },
            result.Sections[0].Elements.Select(element => element switch
            {
                ExtractionDocumentTableElement table => "table:" + CellText(table.Cells[0, 0]),
                _ => Text(element),
            }));
    }

    [Fact]
    public async Task ExtractAsync_TableExtractionNone_ReturnsRuledGridAsText()
    {
        byte[] pdf = PdfFixture.CreatePage(PdfFixture.Grid(72, 700, 100, 20, PriceCells));

        ExtractionDocument result = await ExtractAsync(pdf, new() { TableExtraction = PdfPigTableExtractionMode.None });

        Assert.All(result.Sections[0].Elements, element => Assert.IsType<ExtractionDocumentTextElement>(element));
        string text = string.Join(" ", result.Sections[0].Elements.Select(Text));
        Assert.All(PriceCells, cell => Assert.Contains(cell.Item5, text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("box")]
    [InlineData("rule")]
    [InlineData("stacked")]
    public async Task ExtractAsync_RulesThatDoNotFormATable_ReturnsTextOnly(string layout)
    {
        IEnumerable<string> operations = layout switch
        {
            "box" => [.. PdfFixture.Box(66, 660, 400, 720), PdfFixture.Text(72, 700, "Boxed note line one"), PdfFixture.Text(72, 680, "Boxed note line two")],
            "rule" => [PdfFixture.Text(72, 700, "Heading"), PdfFixture.Line(72, 690, 400, 690), PdfFixture.Text(72, 670, "Body text")],
            _ => PdfFixture.Grid(72, 700, 100, 20, (0, 0, 1, 3, "First panel"), (1, 0, 1, 3, "Second panel"), (2, 0, 1, 1, "")),
        };

        ExtractionDocument result = await ExtractAsync(PdfFixture.CreatePage(operations), new());

        Assert.DoesNotContain(result.Sections[0].Elements, element => element is ExtractionDocumentTableElement);
        Assert.NotEmpty(result.Sections[0].Elements);
    }

    [Fact]
    public async Task ExtractAsync_UnruledAlignedTable_DetectedOnlyByLatticeThenStream()
    {
        string[] items = ["Apple", "Pear", "Plum", "Fig", "Kiwi", "Lime", "Date", "Peach"];
        byte[] pdf = PdfFixture.CreatePage(
        [
            PdfFixture.Text(72, 720, "Item"), PdfFixture.Text(220, 720, "Quantity"), PdfFixture.Text(360, 720, "Price"),
            .. items.SelectMany((item, index) => new[]
            {
                PdfFixture.Text(72, 700 - (index * 16), item),
                PdfFixture.Text(220, 700 - (index * 16), (index + 2).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                PdfFixture.Text(360, 700 - (index * 16), $"{index + 1}.25"),
            }),
        ]);

        ExtractionDocument lattice = await ExtractAsync(pdf, new());
        ExtractionDocument stream = await ExtractAsync(pdf, new() { TableExtraction = PdfPigTableExtractionMode.LatticeThenStream });

        Assert.DoesNotContain(lattice.Sections[0].Elements, element => element is ExtractionDocumentTableElement);
        var table = Assert.Single(stream.Sections[0].Elements.OfType<ExtractionDocumentTableElement>());
        Assert.Equal(3, table.Cells.ColumnCount);
        Assert.True(table.Cells.RowCount >= items.Length);
        var cells = table.Cells.Where(cell => cell is not null).Select(cell => CellText(cell)).ToList();
        Assert.Contains("Peach", cells);
        Assert.Contains("8.25", cells);
    }

    [Fact]
    public void Constructor_InvalidOptions_Throws()
    {
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => new PdfPigDocumentExtractionClient(null!)).ParamName);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPigDocumentExtractionClient(
            new PdfPigDocumentExtractionClientOptions { TableExtraction = (PdfPigTableExtractionMode)42 }));
        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public async Task Constructor_Options_AreCopied()
    {
        var options = new PdfPigDocumentExtractionClientOptions();
        using var client = new PdfPigDocumentExtractionClient(options);
        options.TableExtraction = PdfPigTableExtractionMode.None;
        using var input = new MemoryStream(PdfFixture.CreatePage(PdfFixture.Grid(72, 700, 100, 20, PriceCells)));

        ExtractionDocument result = await client.ExtractAsync(input, "copied", "application/pdf");

        Assert.Single(result.Sections[0].Elements.OfType<ExtractionDocumentTableElement>());
    }

    private static async Task<ExtractionDocument> ExtractAsync(byte[] pdf, PdfPigDocumentExtractionClientOptions options)
    {
        using var client = new PdfPigDocumentExtractionClient(options);
        using var input = new MemoryStream(pdf);
        return await client.ExtractAsync(input, "fixture.pdf", "application/pdf");
    }

    private static string Text(ExtractionDocumentElement element) =>
        Assert.IsType<TextContent>(Assert.IsType<ExtractionDocumentTextElement>(element).Content).Text;

    private static string? CellText(ExtractionDocumentTableCell? cell) =>
        cell?.Content is null ? null : Text(cell.Content);

    private static bool Inside(IReadOnlyList<ExtractionDocumentPoint> inner, IReadOnlyList<ExtractionDocumentPoint> outer) =>
        inner.All(point => point.X >= outer.Min(p => p.X) && point.X <= outer.Max(p => p.X) &&
            point.Y >= outer.Min(p => p.Y) && point.Y <= outer.Max(p => p.Y));
}
