// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Tabula;
using Tabula.Detectors;
using Tabula.Extractors;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.Util;

namespace CommunityToolkit.DocumentExtraction.PdfPig;

/// <summary>Extracts native PDF text, tables and source geometry. This provider does not perform OCR.</summary>
public sealed class PdfPigDocumentExtractionClient : DocumentExtractionClient
{
    private const string ProviderName = "pdfpig";
    private const int MaximumTableGridPositions = 65_536;
    private const double GridTolerance = 1.0;
    private const double WordTolerance = 0.5;

    private readonly IPageSegmenter _pageSegmenter;
    private readonly IWordExtractor? _wordExtractor;
    private readonly Func<TextBlock, string?>? _roleResolver;
    private readonly PdfPigTableExtractionMode _tableExtraction;

    /// <summary>Initializes a new instance of the <see cref="PdfPigDocumentExtractionClient"/> class.</summary>
    /// <param name="pageSegmenter">The segmenter that groups words into text blocks. Defaults to PdfPig's <c>DefaultPageSegmenter</c>.</param>
    /// <param name="wordExtractor">The word extractor. Defaults to PdfPig's built-in word extraction.</param>
    /// <param name="roleResolver">A callback that assigns a role, such as <c>heading</c>, to a text block.</param>
    /// <remarks>Tables are detected with <see cref="PdfPigTableExtractionMode.Lattice"/>. Use <see cref="PdfPigDocumentExtractionClientOptions"/> for other settings.</remarks>
    public PdfPigDocumentExtractionClient(
        IPageSegmenter? pageSegmenter = null,
        IWordExtractor? wordExtractor = null,
        Func<TextBlock, string?>? roleResolver = null)
        : this(new PdfPigDocumentExtractionClientOptions
        {
            PageSegmenter = pageSegmenter,
            WordExtractor = wordExtractor,
            RoleResolver = roleResolver,
        })
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfPigDocumentExtractionClient"/> class.</summary>
    /// <param name="options">The extraction settings. Values are copied; later changes have no effect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="PdfPigDocumentExtractionClientOptions.TableExtraction"/> is not a defined value.</exception>
    public PdfPigDocumentExtractionClient(PdfPigDocumentExtractionClientOptions options)
    {
        _ = Throw.IfNull(options);
        if (!Enum.IsDefined(typeof(PdfPigTableExtractionMode), options.TableExtraction))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.TableExtraction,
                $"{nameof(options.TableExtraction)} must be a defined {nameof(PdfPigTableExtractionMode)} value.");
        }

        _pageSegmenter = options.PageSegmenter ?? DefaultPageSegmenter.Instance;
        _wordExtractor = options.WordExtractor;
        _roleResolver = options.RoleResolver;
        _tableExtraction = options.TableExtraction;
    }

    /// <inheritdoc/>
    public override async Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType,
        DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("PdfPig supports application/pdf only.");
        }

        DocumentExtractionOptions request = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(request, nameof(PdfPigDocumentExtractionClient));
        _ = ProviderAdapterHelpers.ResolveExtractor(request, ProviderName, nameof(PdfPigDocumentExtractionClient), false);
        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        using var pdfInput = new MemoryStream(buffer.ToArray(), writable: false);
        using PdfDocument pdf = PdfDocument.Open(pdfInput);
        var output = new ExtractionDocument(identifier);
        for (int pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(pageNumber);
            (ExtractionDocumentSourcePage frame, _) = ProviderAdapterHelpers.CreatePageFrame(
                identifier, ProviderName, pageNumber, page.Width, page.Height,
                ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft);
            output.SourcePages.Add(frame);
            var section = new ExtractionDocumentSection();
            section.Sources.Add(new ExtractionDocumentSourceReference(frame));
            output.Sections.Add(section);
            IEnumerable<Word> words = _wordExtractor is null ? page.GetWords() : page.GetWords(_wordExtractor);
            if (_tableExtraction == PdfPigTableExtractionMode.None)
            {
                foreach (var block in _pageSegmenter.GetBlocks(words))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddTextBlock(section, frame, block);
                }

                continue;
            }

            List<Word> pageWords = words.ToList();
            bool[] consumed = new bool[pageWords.Count];
            List<PageTable> tables = DetectTables(page, frame, pageWords, consumed, cancellationToken);
            int nextTable = 0;
            foreach (List<Word> band in GetTextBands(pageWords, consumed, tables))
            {
                foreach (var block in _pageSegmenter.GetBlocks(band))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (nextTable < tables.Count && tables[nextTable].Bounds.Top > block.BoundingBox.Top)
                    {
                        AddTable(section, tables[nextTable++]);
                    }

                    AddTextBlock(section, frame, block);
                }
            }

            while (nextTable < tables.Count)
            {
                AddTable(section, tables[nextTable++]);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    // Words separated by a table are segmented separately so that a text block never spans a table.
    // A word's band is the number of tables whose bottom edge lies above it; without tables there is one band.
    private static IEnumerable<List<Word>> GetTextBands(List<Word> words, bool[] consumed, List<PageTable> tables)
    {
        if (tables.Count == 0)
        {
            yield return words;
            yield break;
        }

        var bands = new SortedDictionary<int, List<Word>>();
        for (int index = 0; index < words.Count; index++)
        {
            if (!consumed[index])
            {
                double center = words[index].BoundingBox.Centroid.Y;
                int band = tables.Count(table => table.Bounds.Bottom > center);
                if (!bands.TryGetValue(band, out List<Word>? list))
                {
                    bands[band] = list = [];
                }

                list.Add(words[index]);
            }
        }

        foreach (List<Word> band in bands.Values)
        {
            yield return band;
        }
    }

    private void AddTextBlock(ExtractionDocumentSection section, ExtractionDocumentSourcePage frame, TextBlock block)
    {
        ProviderAdapterHelpers.AddElement(section,
            new ExtractionDocumentTextElement(new TextContent(block.Text),
                role: ProviderAdapterHelpers.NormalizeTextRole(_roleResolver?.Invoke(block))),
            [CreateSource(frame, block.BoundingBox, block)]);
    }

    private static void AddTable(ExtractionDocumentSection section, PageTable table) =>
        ProviderAdapterHelpers.AddElement(section, table.Element, [table.Source]);

    private List<PageTable> DetectTables(
        Page page, ExtractionDocumentSourcePage frame, List<Word> words, bool[] consumed, CancellationToken cancellationToken)
    {
        PageArea area = ObjectExtractor.ExtractPage(page);
        var accepted = new List<PageTable>();
        foreach (Table table in new SpreadsheetExtractionAlgorithm().Extract(area))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Accept(accepted, table, InferSpannedGrid(table), frame, words, consumed);
        }

        if (_tableExtraction == PdfPigTableExtractionMode.LatticeThenStream)
        {
            foreach (TableRectangle region in new SimpleNurminenDetectionAlgorithm().Detect(area))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (accepted.Any(table => Overlaps(table.Bounds, region.BoundingBox)))
                {
                    continue;
                }

                foreach (Table table in new BasicExtractionAlgorithm().Extract(area.GetArea(region.BoundingBox)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Accept(accepted, table, GetTabulaGrid(table), frame, words, consumed);
                }
            }
        }

        // Reading order is top to bottom; tables keep detection order when their tops are equal.
        return accepted.Select((table, index) => (table, index))
            .OrderByDescending(item => item.table.Bounds.Top)
            .ThenBy(item => item.index)
            .Select(item => item.table)
            .ToList();
    }

    // Cell text is built from the page's words, so each word appears exactly once: in one cell or in a text block.
    private static void Accept(
        List<PageTable> accepted, Table table, List<GridCell>? grid,
        ExtractionDocumentSourcePage frame, List<Word> words, bool[] consumed)
    {
        PdfRectangle tableBounds = table.BoundingBox;
        if (grid is null || grid.Count == 0 || accepted.Any(other => Overlaps(other.Bounds, tableBounds)))
        {
            return;
        }

        int rows = grid.Max(cell => cell.Row + cell.RowSpan);
        int columns = grid.Max(cell => cell.Column + cell.ColumnSpan);
        if (rows < 2 || columns < 2 || (long)rows * columns > MaximumTableGridPositions)
        {
            return;
        }

        var cellWords = new List<int>?[grid.Count];
        var cellHasText = new bool[grid.Count];
        for (int index = 0; index < words.Count; index++)
        {
            if (consumed[index])
            {
                continue;
            }

            PdfPoint center = words[index].BoundingBox.Centroid;
            int cell = grid.FindIndex(candidate => Contains(candidate.Source.BoundingBox, center));
            if (cell >= 0)
            {
                (cellWords[cell] ??= []).Add(index);
                cellHasText[cell] |= !string.IsNullOrWhiteSpace(words[index].Text);
            }
        }

        // Text confined to one row or one column, such as a boxed label or stacked form panels, is not a table.
        List<GridCell> textCells = grid.Where((_, index) => cellHasText[index]).ToList();
        if (textCells.Select(cell => cell.Row).Distinct().Take(2).Count() < 2 ||
            textCells.Select(cell => cell.Column).Distinct().Take(2).Count() < 2)
        {
            return;
        }

        var element = new ExtractionDocumentTableElement(null, rows, columns);
        for (int index = 0; index < grid.Count; index++)
        {
            GridCell cell = grid[index];
            ExtractionDocumentSourceReference source = CreateSource(frame, cell.Source.BoundingBox, cell.Source);
            ExtractionDocumentTableCell mapped;
            if (cellWords[index] is { } assigned)
            {
                foreach (int word in assigned)
                {
                    consumed[word] = true;
                }
            }

            if (!cellHasText[index])
            {
                mapped = new ExtractionDocumentTableCell(cell.RowSpan, cell.ColumnSpan, null);
            }
            else
            {
                var content = new ExtractionDocumentTextElement(new TextContent(JoinLines(cellWords[index]!.Select(word => words[word]))));
                content.Sources.Add(source);
                mapped = new ExtractionDocumentTableCell(content, cell.RowSpan, cell.ColumnSpan, null);
            }

            mapped.Sources.Add(source);
            element.Cells[cell.Row, cell.Column] = mapped;
        }

        accepted.Add(new PageTable(element, CreateSource(frame, tableBounds, table), tableBounds));
    }

    // Tabula indexes merged lattice cells as if they were single cells, so rebuild the grid from the ruled cell edges.
    // Cells nested inside another cell, such as padding or fill boxes, belong to the enclosing cell.
    // Other overlapping cells cannot form a grid and are not treated as tables.
    private static List<GridCell>? InferSpannedGrid(Table table)
    {
        List<Cell> candidates = table.Cells.Where(cell => !IsEmpty(cell)).ToList();
        var cells = new List<Cell>(candidates.Count);
        for (int index = 0; index < candidates.Count; index++)
        {
            bool nested = false;
            for (int other = 0; other < candidates.Count && !nested; other++)
            {
                // Identical cells keep the first occurrence.
                nested = other != index && Encloses(candidates[other], candidates[index]) &&
                    (other < index || !Encloses(candidates[index], candidates[other]));
            }

            if (!nested)
            {
                cells.Add(candidates[index]);
            }
        }

        if (cells.Count == 0)
        {
            return null;
        }

        List<(double Min, double Max)> columns = Cluster(cells.SelectMany(cell => new[] { cell.Left, cell.Right }));
        List<(double Min, double Max)> rows = Cluster(cells.SelectMany(cell => new[] { cell.Top, cell.Bottom }));
        rows.Reverse();
        var grid = new List<GridCell>(cells.Count);
        var occupied = new HashSet<(int, int)>();
        foreach (Cell cell in cells)
        {
            int column = IndexOf(columns, cell.Left);
            int columnSpan = IndexOf(columns, cell.Right) - column;
            int row = IndexOf(rows, cell.Top);
            int rowSpan = IndexOf(rows, cell.Bottom) - row;
            if (columnSpan < 1 || rowSpan < 1)
            {
                return null;
            }

            for (int r = row; r < row + rowSpan; r++)
            {
                for (int c = column; c < column + columnSpan; c++)
                {
                    if (!occupied.Add((r, c)))
                    {
                        return null;
                    }
                }
            }

            grid.Add(new GridCell(cell, row, column, rowSpan, columnSpan));
        }

        return grid;
    }

    private static List<GridCell> GetTabulaGrid(Table table)
    {
        var grid = new List<GridCell>();
        for (int row = 0; row < table.RowCount; row++)
        {
            for (int column = 0; column < table.ColumnCount; column++)
            {
                Cell cell = table[row, column];
                if (!IsEmpty(cell))
                {
                    grid.Add(new GridCell(cell, row, column, 1, 1));
                }
            }
        }

        return grid;
    }

    private static List<(double Min, double Max)> Cluster(IEnumerable<double> values)
    {
        var clusters = new List<(double Min, double Max)>();
        foreach (double value in values.OrderBy(value => value))
        {
            if (clusters.Count > 0 && value - clusters[clusters.Count - 1].Min <= GridTolerance)
            {
                clusters[clusters.Count - 1] = (clusters[clusters.Count - 1].Min, value);
            }
            else
            {
                clusters.Add((value, value));
            }
        }

        return clusters;
    }

    private static int IndexOf(List<(double Min, double Max)> clusters, double value) =>
        clusters.FindIndex(cluster => value >= cluster.Min && value <= cluster.Max);

    private static bool IsEmpty(Cell cell) =>
        cell.IsPlaceholder || cell.Right - cell.Left <= 0 || cell.Top - cell.Bottom <= 0;

    // Orders a cell's words top to bottom by line, then left to right, joined by single spaces.
    private static string JoinLines(IEnumerable<Word> words)
    {
        var lines = new List<(double Top, double Bottom, List<Word> Words)>();
        foreach (Word word in words.OrderByDescending(word => word.BoundingBox.Top))
        {
            PdfRectangle bounds = word.BoundingBox;
            int line = lines.FindIndex(candidate =>
                Math.Min(candidate.Top, bounds.Top) - Math.Max(candidate.Bottom, bounds.Bottom) >=
                0.5 * Math.Min(candidate.Top - candidate.Bottom, bounds.Top - bounds.Bottom));
            if (line < 0)
            {
                lines.Add((bounds.Top, bounds.Bottom, [word]));
            }
            else
            {
                lines[line].Words.Add(word);
            }
        }

        var builder = new StringBuilder();
        foreach (Word word in lines.SelectMany(line => line.Words.OrderBy(word => word.BoundingBox.Left)))
        {
            if (!string.IsNullOrWhiteSpace(word.Text))
            {
                _ = builder.Append(builder.Length == 0 ? string.Empty : " ").Append(word.Text.Trim());
            }
        }

        return builder.ToString();
    }

    private static bool Encloses(Cell outer, Cell inner) =>
        inner.Left >= outer.Left - GridTolerance && inner.Right <= outer.Right + GridTolerance &&
        inner.Bottom >= outer.Bottom - GridTolerance && inner.Top <= outer.Top + GridTolerance;

    private static bool Contains(PdfRectangle bounds, PdfPoint point) =>
        point.X >= bounds.Left - WordTolerance && point.X <= bounds.Right + WordTolerance &&
        point.Y >= bounds.Bottom - WordTolerance && point.Y <= bounds.Top + WordTolerance;

    private static bool Overlaps(PdfRectangle first, PdfRectangle second) =>
        Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left) > WordTolerance &&
        Math.Min(first.Top, second.Top) - Math.Max(first.Bottom, second.Bottom) > WordTolerance;

    private static ExtractionDocumentSourceReference CreateSource(
        ExtractionDocumentSourcePage frame, PdfRectangle bounds, object rawRepresentation) =>
        new(frame,
            [
                new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom),
                new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Top),
            ], rawRepresentation);

    private sealed record GridCell(Cell Source, int Row, int Column, int RowSpan, int ColumnSpan);

    private sealed record PageTable(
        ExtractionDocumentTableElement Element, ExtractionDocumentSourceReference Source, PdfRectangle Bounds);
}
