using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig;

namespace CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;

// Regression tests over the pinned ParseBench-2 pages; see Corpus/ParseBench-2/README.md. These are fixtures, not a benchmark.
public sealed class PdfPigCorpusTests(ParseBench2Corpus corpus) : IClassFixture<ParseBench2Corpus>
{
    private static readonly PdfPigTableExtractionMode[] AllModes =
        [PdfPigTableExtractionMode.None, PdfPigTableExtractionMode.Lattice, PdfPigTableExtractionMode.LatticeThenStream];

    public static TheoryData<string> Pages => [.. ParseBench2Corpus.Manifest.Pages.Select(page => page.Id)];

    public static TheoryData<string> TablePages => [.. ParseBench2Corpus.Manifest.Pages.Where(page => page.Set == "tables").Select(page => page.Id)];

    public static TheoryData<string> NegativePages => [.. ParseBench2Corpus.Manifest.Pages.Where(page => page.Set == "negative").Select(page => page.Id)];

    public static TheoryData<string, PdfPigTableExtractionMode> PageModes =>
        [.. ParseBench2Corpus.Manifest.Pages.SelectMany(page => AllModes.Select(mode => (page.Id, mode)))];

    public static TheoryData<string, PdfPigTableExtractionMode> PageTableModes =>
        [.. ParseBench2Corpus.Manifest.Pages.SelectMany(page => AllModes.Skip(1).Select(mode => (page.Id, mode)))];

    [Fact]
    public void Manifest_DescribesTenTablePagesAndFiveNegativeControls()
    {
        var manifest = ParseBench2Corpus.Manifest;
        Assert.Equal("llamaindex/ParseBench-2", manifest.Dataset);
        Assert.Equal("Apache-2.0", manifest.License);
        Assert.Equal(manifest.Pages.Length, manifest.Pages.Select(page => page.Id).Distinct().Count());
        Assert.Equal(10, manifest.Pages.Count(page => page.Set == "tables"));
        Assert.Equal(5, manifest.Pages.Count(page => page.Set == "negative"));
        Assert.All(manifest.Pages.Where(page => page.Set == "tables"), page =>
        {
            Assert.NotNull(page.FactMode);
            Assert.NotEmpty(page.Facts!);
            Assert.NotEmpty(page.Tables[page.FactMode.Value]);
        });
    }

    // C1
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Corpus_CachedFile_MatchesPinnedHash(string id)
    {
        CorpusPage page = ParseBench2Corpus.GetPage(id);

        Assert.Equal(page.Sha256, await ParseBench2Corpus.HashAsync(corpus.GetPath(page), TestContext.Current.CancellationToken));
    }

    // C2, C3
    [Theory]
    [MemberData(nameof(PageModes))]
    public async Task ExtractAsync_EveryMode_KeepsEachCharacterExactlyOnce(string id, PdfPigTableExtractionMode mode)
    {
        CorpusPage page = ParseBench2Corpus.GetPage(id);

        ExtractionDocument result = await corpus.ExtractAsync(page, mode);

        Assert.Single(result.SourcePages);
        var expected = new StringBuilder();
        using (PdfDocument pdf = PdfDocument.Open(corpus.GetPath(page)))
        {
            foreach (var word in pdf.GetPage(1).GetWords())
            {
                expected.Append(word.Text);
            }
        }

        var actual = new StringBuilder();
        foreach (ExtractionDocumentElement element in Assert.Single(result.Sections).Elements)
        {
            if (element is ExtractionDocumentTableElement table)
            {
                foreach (ExtractionDocumentTableCell cell in Cells(table))
                {
                    actual.Append(CellText(cell));
                }
            }
            else
            {
                actual.Append(Text(element));
            }
        }

        Assert.Equal(Characters(expected.ToString()), Characters(actual.ToString()));
    }

    // C4
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task ExtractAsync_None_MatchesTextFromBeforeTableExtraction(string id)
    {
        CorpusPage page = ParseBench2Corpus.GetPage(id);

        ExtractionDocument result = await corpus.ExtractAsync(page, PdfPigTableExtractionMode.None);

        Assert.Empty(result.EnumerateContent().OfType<ExtractionDocumentTableElement>());
        string text = string.Join("\n", result.EnumerateContent().OfType<ExtractionDocumentTextElement>()
            .Select(element => Text(element).Replace("\r\n", "\n", StringComparison.Ordinal)));
        Assert.Equal(page.NoneTextSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    // C5, C6
    [Theory]
    [MemberData(nameof(PageTableModes))]
    public async Task ExtractAsync_TableModes_ReturnPinnedTableDimensions(string id, PdfPigTableExtractionMode mode)
    {
        CorpusPage page = ParseBench2Corpus.GetPage(id);

        ExtractionDocument result = await corpus.ExtractAsync(page, mode);

        Assert.Equal(page.Tables[mode], Tables(result).Select(table => $"{table.Cells.RowCount}x{table.Cells.ColumnCount}"));
    }

    // C5
    [Theory]
    [MemberData(nameof(TablePages))]
    public async Task ExtractAsync_TablePages_SatisfyReferenceFacts(string id)
    {
        CorpusPage page = ParseBench2Corpus.GetPage(id);

        ExtractionDocument result = await corpus.ExtractAsync(page, page.FactMode!.Value);

        var cells = Tables(result).SelectMany(table => Cells(table).Select(cell => (Table: table, Cell: cell, Text: Normalize(CellText(cell))))).ToList();
        Assert.All(page.Facts!, fact =>
        {
            var anchor = Unique(fact.Anchor);
            var sameTable = cells.Where(other => other.Table == anchor.Table && other.Text.Length > 0).ToList();
            switch (fact.Kind)
            {
                case "right":
                    Assert.Equal(Normalize(fact.Expected), sameTable
                        .Where(other => Overlap(other.Cell.RowIndex, other.Cell.RowSpan, anchor.Cell.RowIndex, anchor.Cell.RowSpan) &&
                            other.Cell.ColumnIndex >= anchor.Cell.ColumnIndex + anchor.Cell.ColumnSpan)
                        .OrderBy(other => other.Cell.ColumnIndex).ThenBy(other => other.Cell.RowIndex).Select(other => other.Text).FirstOrDefault());
                    break;
                case "down":
                    Assert.Equal(Normalize(fact.Expected), sameTable
                        .Where(other => Overlap(other.Cell.ColumnIndex, other.Cell.ColumnSpan, anchor.Cell.ColumnIndex, anchor.Cell.ColumnSpan) &&
                            other.Cell.RowIndex >= anchor.Cell.RowIndex + anchor.Cell.RowSpan)
                        .OrderBy(other => other.Cell.RowIndex).ThenBy(other => other.Cell.ColumnIndex).Select(other => other.Text).FirstOrDefault());
                    break;
                case "span":
                    Assert.Equal((fact.RowSpan, fact.ColumnSpan), ((int?)anchor.Cell.RowSpan, (int?)anchor.Cell.ColumnSpan));
                    break;
                case "headerChain":
                    Assert.All(fact.Headers!, header => Assert.Contains(sameTable, other => other.Text == Normalize(header) &&
                        other.Cell.RowIndex < anchor.Cell.RowIndex &&
                        Overlap(other.Cell.ColumnIndex, other.Cell.ColumnSpan, anchor.Cell.ColumnIndex, 1)));
                    break;
                case "sameColumn":
                    var expected = Unique(fact.Expected!);
                    Assert.Same(anchor.Table, expected.Table);
                    Assert.True(Overlap(anchor.Cell.ColumnIndex, anchor.Cell.ColumnSpan, expected.Cell.ColumnIndex, expected.Cell.ColumnSpan));
                    break;
                default:
                    Assert.Fail($"Unknown fact kind {fact.Kind}.");
                    break;
            }
        });

        (ExtractionDocumentTableElement Table, ExtractionDocumentTableCell Cell, string Text) Unique(string text) =>
            Assert.Single(cells, cell => cell.Text == Normalize(text));
    }

    // C6
    [Theory]
    [MemberData(nameof(NegativePages))]
    public async Task ExtractAsync_NegativeControls_ReturnNoLatticeTables(string id)
    {
        CorpusPage page = ParseBench2Corpus.GetPage(id);

        Assert.Empty(Tables(await corpus.ExtractAsync(page, PdfPigTableExtractionMode.Lattice)));
        Assert.Equal(page.Tables[PdfPigTableExtractionMode.LatticeThenStream].Length > 0, !string.IsNullOrWhiteSpace(page.StreamJustification));
    }

    // C7
    [Theory]
    [MemberData(nameof(PageTableModes))]
    public async Task ExtractAsync_TableModes_ReturnWellFormedTablesTopToBottom(string id, PdfPigTableExtractionMode mode)
    {
        ExtractionDocument result = await corpus.ExtractAsync(ParseBench2Corpus.GetPage(id), mode);

        var tables = Tables(result).ToList();
        Assert.Equal(tables.Select(table => -Bounds(table).Top), tables.Select(table => -Bounds(table).Top).Order());
        Assert.All(tables, table =>
        {
            var covered = new bool[table.Cells.RowCount, table.Cells.ColumnCount];
            var tableBounds = Bounds(table);
            foreach (ExtractionDocumentTableCell cell in Cells(table))
            {
                Assert.Same(cell, table.Cells[cell.RowIndex, cell.ColumnIndex]);
                Assert.InRange(cell.RowIndex + cell.RowSpan, 1, table.Cells.RowCount);
                Assert.InRange(cell.ColumnIndex + cell.ColumnSpan, 1, table.Cells.ColumnCount);
                for (int row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
                {
                    for (int column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
                    {
                        Assert.False(covered[row, column], $"Cell ({row}, {column}) of {id} is covered twice.");
                        covered[row, column] = true;
                    }
                }

                var cellBounds = Bounds(Assert.Single(cell.Sources).Polygon);
                Assert.True(cellBounds.Left >= tableBounds.Left - 1 && cellBounds.Right <= tableBounds.Right + 1 &&
                    cellBounds.Bottom >= tableBounds.Bottom - 1 && cellBounds.Top <= tableBounds.Top + 1,
                    $"Cell ({cell.RowIndex}, {cell.ColumnIndex}) of {id} lies outside its table.");
            }
        });
    }

    // C8
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task ExtractAsync_SameInput_IsDeterministic(string id)
    {
        CorpusPage page = ParseBench2Corpus.GetPage(id);

        ExtractionDocument first = await corpus.ExtractAsync(page, PdfPigTableExtractionMode.LatticeThenStream);
        ExtractionDocument second = await corpus.ExtractFreshAsync(page, PdfPigTableExtractionMode.LatticeThenStream);

        Assert.Equal(Describe(first), Describe(second));
    }

    private static IEnumerable<ExtractionDocumentTableElement> Tables(ExtractionDocument document) =>
        document.Sections.SelectMany(section => section.Elements).OfType<ExtractionDocumentTableElement>();

    private static IEnumerable<ExtractionDocumentTableCell> Cells(ExtractionDocumentTableElement table) =>
        table.Cells.OfType<ExtractionDocumentTableCell>().Distinct();

    private static string Text(ExtractionDocumentElement element) =>
        Assert.IsType<TextContent>(Assert.IsType<ExtractionDocumentTextElement>(element).Content).Text;

    private static string? CellText(ExtractionDocumentTableCell cell) => cell.Content is null ? null : Text(cell.Content);

    private static string Normalize(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static bool Overlap(int start, int length, int otherStart, int otherLength) =>
        start < otherStart + otherLength && otherStart < start + length;

    private static string Characters(string text) =>
        new([.. text.Where(character => !char.IsWhiteSpace(character)).Order()]);

    private static (double Left, double Bottom, double Right, double Top) Bounds(ExtractionDocumentTableElement table) =>
        Bounds(Assert.Single(table.Sources).Polygon);

    private static (double Left, double Bottom, double Right, double Top) Bounds(IReadOnlyList<ExtractionDocumentPoint> polygon) =>
        (polygon.Min(point => point.X), polygon.Min(point => point.Y), polygon.Max(point => point.X), polygon.Max(point => point.Y));

    private static string Describe(ExtractionDocument document)
    {
        var builder = new StringBuilder();
        foreach (ExtractionDocumentElement element in document.Sections.SelectMany(section => section.Elements))
        {
            builder.Append(string.Join(',', element.Sources.SelectMany(source => source.Polygon).Select(point => $"{point.X:R} {point.Y:R}"))).Append('|');
            if (element is ExtractionDocumentTableElement table)
            {
                builder.Append($"table {table.Cells.RowCount}x{table.Cells.ColumnCount}");
                foreach (ExtractionDocumentTableCell cell in Cells(table))
                {
                    builder.Append($"[{cell.RowIndex},{cell.ColumnIndex},{cell.RowSpan},{cell.ColumnSpan}]{CellText(cell)}");
                }
            }
            else
            {
                builder.Append(Text(element));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}

public sealed class ParseBench2CorpusCacheTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ctk-corpus-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task EnsureFileAsync_HashMismatch_ThrowsAndCachesNothing()
    {
        string path = Path.Combine(_directory, "page.pdf");

        await Assert.ThrowsAsync<InvalidDataException>(() => ParseBench2Corpus.EnsureFileAsync(
            path, Hash("expected"), (temporary, _) => File.WriteAllTextAsync(temporary, "tampered"), TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public async Task EnsureFileAsync_CachedFile_IsReusedOrReplacedByHash()
    {
        string path = Path.Combine(_directory, "page.pdf");
        int downloads = 0;
        Task Download(string temporary, CancellationToken token)
        {
            downloads++;
            return File.WriteAllTextAsync(temporary, "expected", token);
        }

        await File.WriteAllTextAsync(path, "stale", TestContext.Current.CancellationToken);
        await ParseBench2Corpus.EnsureFileAsync(path, Hash("expected"), Download, TestContext.Current.CancellationToken);
        await ParseBench2Corpus.EnsureFileAsync(path, Hash("expected"), Download, TestContext.Current.CancellationToken);

        Assert.Equal(1, downloads);
        Assert.Equal("expected", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
