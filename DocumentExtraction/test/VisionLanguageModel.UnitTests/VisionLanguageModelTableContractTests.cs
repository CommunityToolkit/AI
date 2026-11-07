using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.VisionLanguageModel.UnitTests;

public sealed class VisionLanguageModelTableContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task ExtractAsync_GeneratedMergedGrids_DerivesExtentAndPreservesEveryObservation(int seed)
    {
        var random = new Random(seed);
        int rows = random.Next(2, 10);
        int columns = random.Next(2, 12);
        var occupied = new bool[rows, columns];
        var cells = new List<JsonObject>();
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                if (occupied[row, column])
                {
                    continue;
                }

                int columnSpan = 1;
                while (column + columnSpan < columns && !occupied[row, column + columnSpan] && random.Next(2) == 0)
                {
                    columnSpan++;
                }

                int rowSpan = 1;
                if (row + 1 < rows && random.Next(2) == 0 &&
                    Enumerable.Range(column, columnSpan).All(c => !occupied[row + 1, c]))
                {
                    rowSpan++;
                }

                for (int r = row; r < row + rowSpan; r++)
                {
                    for (int c = column; c < column + columnSpan; c++)
                    {
                        occupied[r, c] = true;
                    }
                }

                string? text = cells.Count % 3 == 0 ? null : cells.Count % 3 == 1 ? "" : $"observed:{row},{column}";
                cells.Add(new JsonObject
                {
                    ["rowIndex"] = row, ["columnIndex"] = column,
                    ["rowSpan"] = rowSpan, ["columnSpan"] = columnSpan,
                    ["text"] = text, ["role"] = row == 0 ? "columnHeader" : column == 0 ? "rowHeader" : null,
                });
            }
        }

        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
        var table = new JsonObject { ["cells"] = new JsonArray(cells.Select(cell => (JsonNode)cell).ToArray()) };
        var (document, chat, response) = await Extract(table);
        var mapped = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(document.EnumerateContent()));
        Assert.Equal((rows, columns), (mapped.Cells.RowCount, mapped.Cells.ColumnCount));
        Assert.Equal(cells.Count, mapped.Cells.Count(cell => cell is not null));
        Assert.Null(mapped.Content);
        var source = Assert.Single(mapped.Sources);
        Assert.Null(source.Page.PageNumber);
        Assert.Null(source.Page.Dimensions);
        Assert.Empty(source.Polygon);
        foreach (var native in cells)
        {
            int row = native["rowIndex"]!.GetValue<int>();
            int column = native["columnIndex"]!.GetValue<int>();
            var cell = mapped.Cells[row, column]!;
            Assert.Equal(native["rowSpan"]!.GetValue<int>(), cell.RowSpan);
            Assert.Equal(native["columnSpan"]!.GetValue<int>(), cell.ColumnSpan);
            Assert.Equal(native["role"]?.GetValue<string>(), cell.Role);
            if (native["text"] is null)
            {
                Assert.Null(cell.Content);
            }
            else
            {
                Assert.Equal(native["text"]!.GetValue<string>(), Assert.IsType<TextContent>(cell.Content!.Content).Text);
                Assert.Same(source, Assert.Single(cell.Content.Sources));
            }

            Assert.Same(source, Assert.Single(cell.Sources));
            for (int r = row; r < row + cell.RowSpan; r++)
            {
                for (int c = column; c < column + cell.ColumnSpan; c++)
                {
                    if (r != row || c != column)
                    {
                        Assert.Null(mapped.Cells[r, c]);
                    }
                }
            }
        }

        Assert.Single(chat.Requests);
        Assert.Same(response, document.RawRepresentation);
    }

    [Theory]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0,"rowSpan":0}]}""", ".cells[0].rowSpan")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0,"columnSpan":-1}]}""", ".cells[0].columnSpan")]
    [InlineData("""{"cells":[{"rowIndex":-1,"columnIndex":0}]}""", ".cells[0].rowIndex")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0.5}]}""", ".cells[0].columnIndex")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0,"rowSpan":null}]}""", ".cells[0].rowSpan")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0,"text":3}]}""", ".cells[0].text")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0,"role":true}]}""", ".cells[0].role")]
    [InlineData("""{"cells":[{"rowIndex":2147483647,"columnIndex":0}]}""", ".cells[0]")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":2147483647,"columnSpan":2147483647}]}""", ".cells[0]")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0},{"rowIndex":0,"columnIndex":0}]}""", ".cells[1]")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0,"columnSpan":2},{"rowIndex":0,"columnIndex":1}]}""", ".cells[1]")]
    [InlineData("""{"cells":[{"rowIndex":1,"columnIndex":1},{"rowIndex":0,"columnIndex":0,"rowSpan":2,"columnSpan":2}]}""", ".cells[1]")]
    [InlineData("""{"cells":[{"rowIndex":0}]}""", ".cells[0].columnIndex")]
    [InlineData("""{"cells":[null]}""", ".cells[0]")]
    [InlineData("""{"cells":null}""", "requires")]
    [InlineData("""{"cells":null,"markdown":null}""", "requires")]
    [InlineData("""{"cells":null,"markdown":3}""", ".markdown")]
    [InlineData("""{"cells":{} }""", ".cells")]
    [InlineData("""{"cells":[],"markdown":"do not discard me"}""", "cannot combine")]
    [InlineData("""{"cells":[],"markdown":""}""", "cannot combine")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0}],"markdown":"literal"}""", "cannot combine")]
    [InlineData("""{"cells":[{"rowIndex":0,"columnIndex":0,"unexpected":"do not discard me"}]}""", ".cells[0]")]
    [InlineData("""{"cells":[],"unexpected":true}""", "unsupported")]
    [InlineData("""{"cells":[],"cells":[]}""", "duplicate property")]
    [InlineData("""{"cells":[{"rowIndex":0,"rowIndex":1,"columnIndex":0}]}""", ".cells[0]")]
    [InlineData("""{"markdown":null}""", "requires")]
    [InlineData("""{}""", "requires")]
    public async Task ExtractAsync_InvalidNewContract_ThrowsContextualInvalidDataWithoutRepairOrRetry(string table, string detail)
    {
        var chat = new RecordingChatClient(RecordingChatClient.Response(
            $$"""{"pages":[{},{"tables":[{"cells":[]},{{table}}]}]}"""));
        using var input = new BorrowedInput([1, 2, 3]);
        using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { UseStructuredOutput = true, Prompt = "custom contract" });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "bad.pdf", "application/pdf"));
        Assert.Contains("pages[1].tables[1]", error.Message);
        Assert.Contains(detail, error.Message);
        Assert.Single(chat.Requests);
        Assert.Equal("custom contract", Assert.IsType<TextContent>(chat.Requests[0].Messages[0].Contents[0]).Text);
        client.Dispose();
        input.AssertStillReadable(1);
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Theory]
    [InlineData(6, 9, 6, 8)]
    [InlineData(6, 13, 6, 14)]
    [InlineData(1, 1, 0, 0)]
    public async Task ExtractAsync_OldCountBearingContract_RejectsRatherThanResizesEvenWhenConsistent(
        int rows, int columns, int row, int column)
    {
        var table = new JsonObject
        {
            ["rowCount"] = rows, ["columnCount"] = columns,
            ["cells"] = new JsonArray(new JsonObject { ["rowIndex"] = row, ["columnIndex"] = column, ["text"] = "observed" }),
        };
        var chat = new RecordingChatClient(RecordingChatClient.Response(Wrap(table)));
        using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { UseStructuredOutput = true });
        using var input = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "old.pdf", "application/pdf"));
        Assert.Contains("pages[0].tables[0]", error.Message);
        Assert.Contains("unsupported property 'rowCount'", error.Message);
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData(0, 65535, true)]
    [InlineData(65535, 0, true)]
    [InlineData(255, 255, true)]
    [InlineData(0, 65536, false)]
    [InlineData(65536, 0, false)]
    [InlineData(256, 255, false)]
    [InlineData(65535, 65535, false)]
    public async Task ExtractAsync_SparseExtentBudget_IsCheckedBeforeRectangularAllocation(int row, int column, bool valid)
    {
        var table = new JsonObject { ["cells"] = new JsonArray(new JsonObject { ["rowIndex"] = row, ["columnIndex"] = column }) };
        if (valid)
        {
            var (result, _, _) = await Extract(table);
            var mapped = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(result.EnumerateContent()));
            Assert.Equal((row + 1, column + 1), (mapped.Cells.RowCount, mapped.Cells.ColumnCount));
            Assert.NotNull(mapped.Cells[row, column]);
            Assert.Equal(1, mapped.Cells.Count(cell => cell is not null));
        }
        else
        {
            var chat = new RecordingChatClient(RecordingChatClient.Response(Wrap(table)));
            using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { UseStructuredOutput = true });
            using var input = new MemoryStream([1]);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "budget.png", "image/png"));
            Assert.Contains("65536", error.Message);
            Assert.Contains("pages[0].tables[0].cells[0]", error.Message);
        }
    }

    [Fact]
    public async Task ExtractAsync_CombinedTableBudget_AppliesAcrossPagesNotJustIndividualTables()
    {
        const string table = """{"cells":[{"rowIndex":0,"columnIndex":0,"rowSpan":256,"columnSpan":128}]}""";
        var response = RecordingChatClient.Response($$"""{"pages":[{"tables":[{{table}}]},{"tables":[{{table}},{"cells":[{"rowIndex":0,"columnIndex":0}]}]}]}""");
        var chat = new RecordingChatClient(response);
        using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { UseStructuredOutput = true });
        using var input = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "budget.pdf", "application/pdf"));
        Assert.Contains("pages[1].tables[1].cells[0]", error.Message);
        Assert.Contains("65536", error.Message);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task ExtractAsync_ExcessiveAnchorArray_IsRejectedBeforeProcessingIndividualCells()
    {
        string table = """{"cells":[""" + string.Join(",", Enumerable.Repeat(
            """{"rowIndex":0,"columnIndex":0}""", 65537)) + "]}";
        var chat = new RecordingChatClient(RecordingChatClient.Response(
            $$"""{"pages":[{"tables":[{{table}}]}]}"""));
        using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { UseStructuredOutput = true });
        using var input = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "large.pdf", "application/pdf"));
        Assert.Contains("pages[0].tables[0].cells", error.Message);
        Assert.Contains("at most 65536 anchors", error.Message);
        Assert.DoesNotContain("duplicate anchor", error.Message);
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData("""{"cells":[]}""", null)]
    [InlineData("""{"cells":[],"markdown":null}""", null)]
    [InlineData("""{"markdown":""}""", "")]
    [InlineData("""{"markdown":"| literal | table |\n|---|---|"}""", "| literal | table |\n|---|---|")]
    [InlineData("""{"cells":null,"markdown":""}""", "")]
    [InlineData("""{"cells":null,"markdown":"| literal | table |\n|---|---|"}""", "| literal | table |\n|---|---|")]
    public async Task ExtractAsync_EmptyAndMarkdownTables_PreserveLiteralContractWithoutInventingDimensions(string table, string? text)
    {
        var (document, _, _) = await Extract(JsonNode.Parse(table)!.AsObject());
        var mapped = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(document.EnumerateContent()));
        Assert.Equal((0, 0), (mapped.Cells.RowCount, mapped.Cells.ColumnCount));
        Assert.Empty(mapped.Cells);
        if (text is null)
        {
            Assert.Null(mapped.Content);
        }
        else
        {
            Assert.Equal(text, Assert.IsType<TextContent>(mapped.Content).Text);
        }
    }

    [Fact]
    public async Task ExtractAsync_DefaultSchemaAndPrompt_DefineSingleAuthorityAndRejectUnknownFieldsAtEveryLevel()
    {
        var (_, chat, _) = await Extract(new JsonObject { ["cells"] = new JsonArray() });
        var request = Assert.Single(chat.Requests);
        string instruction = Assert.IsType<TextContent>(request.Messages[0].Contents[0]).Text;
        Assert.Contains("zero-based anchor positions including header", instruction);
        Assert.Contains("merged cell once at its top-left anchor", instruction);
        Assert.Contains("observed blank cells", instruction);
        Assert.Contains("Do not output rowCount or columnCount", instruction);
        Assert.Contains("For a literal Markdown table, use cells:null", instruction);
        var schema = Assert.IsType<ChatResponseFormatJson>(request.Options!.ResponseFormat).Schema!.Value;
        var page = schema.GetProperty("properties").GetProperty("pages").GetProperty("items");
        var table = page.GetProperty("properties").GetProperty("tables").GetProperty("items");
        var cells = table.GetProperty("properties").GetProperty("cells");
        var cell = cells.GetProperty("items");
        var figure = page.GetProperty("properties").GetProperty("figures").GetProperty("items");
        foreach (var value in new[] { schema, page, table, cell, figure })
        {
            Assert.False(value.GetProperty("additionalProperties").GetBoolean());
        }
        Assert.Equal(65536, cells.GetProperty("maxItems").GetInt32());
        Assert.Equal(new[] { "array", "null" }, cells.GetProperty("type").EnumerateArray().Select(value => value.GetString()));
        Assert.False(table.GetProperty("properties").TryGetProperty("rowCount", out _));
        Assert.False(table.GetProperty("properties").TryGetProperty("columnCount", out _));
    }

    [Theory]
    [InlineData("""{"pages":[{"unmapped":"not ignored"}]}""", "pages[0]")]
    [InlineData("""{"pages":[{"figures":[{"unmapped":"not ignored"}]}]}""", "pages[0].figures[0]")]
    [InlineData("""{"pages":[{"index":2147483647}]}""", "pages[0].index")]
    [InlineData("""{"pages":[{"confidence":"wrong"}]}""", "pages[0].confidence")]
    [InlineData("""{"pages":[{"markdown":42}]}""", "pages[0].markdown")]
    [InlineData("""{"pages":[{"tables":null}]}""", "pages[0].tables")]
    [InlineData("""{"pages":[null]}""", "pages[0]")]
    [InlineData("""{"pages":[],"pages":[]}""", "response")]
    [InlineData("{", "response")]
    public async Task ExtractAsync_MalformedModelOutput_HasContextualInvalidData(string json, string context)
    {
        var chat = new RecordingChatClient(RecordingChatClient.Response(json));
        using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { UseStructuredOutput = true });
        using var input = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "bad.pdf", "application/pdf"));
        Assert.Contains(context, error.Message);
        Assert.Single(chat.Requests);
    }

    private static string Wrap(JsonObject table)
        => new JsonObject { ["pages"] = new JsonArray(new JsonObject { ["tables"] = new JsonArray(table) }) }.ToJsonString();

    private static async Task<(ExtractionDocument, RecordingChatClient, ChatResponse)> Extract(JsonObject table)
    {
        var response = RecordingChatClient.Response(Wrap(table));
        var chat = new RecordingChatClient(response);
        using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { UseStructuredOutput = true });
        using var input = new MemoryStream([1, 2, 3]);
        var document = await client.ExtractAsync(input, "generated.pdf", "application/pdf");
        return (document, chat, response);
    }
}
