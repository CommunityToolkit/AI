using System.Text.Json;
using CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using SkiaSharp;

namespace CommunityToolkit.DocumentExtraction.Vision.UnitTests;

public sealed class VisionStructuredAcceptanceTests
{
    private const string Valid =
        """
        {"pages":[
          {"index":2,"markdown":"# Invoice Ω\nOnly prose, not cell text.","language":"fr","confidence":0.875,
           "tables":[{"rowCount":2,"columnCount":2,"markdown":"must not duplicate cells",
             "cells":[{"rowIndex":0,"columnIndex":0,"text":"Produit","columnSpan":2,"role":"columnHeader"},
                      {"rowIndex":1,"columnIndex":0,"text":"Thé","rowSpan":1,"columnSpan":1},
                      {"rowIndex":1,"columnIndex":1,"text":null,"role":"stub"}]}],
           "figures":[{"caption":"Figure 1: Revenue €"}]},
          {"markdown":"Second unnumbered panel","language":null,"confidence":1,
           "tables":[{"rowCount":0,"columnCount":0,"markdown":"| literal | table |\n|---|---|"}],
           "figures":[{"caption":null}]}]}
        """;

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExtractAsync_ValidStructuredImage_PreservesConcretePayloadSourcesModelRawAndSchema(bool? capture, bool explicitRaw)
    {
        using var native = JsonDocument.Parse(Valid);
        object raw = native.RootElement.Clone();
        var response = RecordingChatClient.Response(Valid, explicitRaw ? raw : null);
        var chat = new RecordingChatClient(response);
        using var input = new BorrowedInput([137, 80, 78, 71, 11, 22, 33]);
        using var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        var result = await client.ExtractAsync(input, "invoice-panels.png", "image/png",
            capture is null ? null : new() { ExtractorName = "requested-structured-route", CaptureRawContent = capture.Value });

        Assert.Equal("invoice-panels.png", result.Identifier);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal(new int?[] { 3, null }, result.SourcePages.Select(p => p.PageNumber));
        Assert.Null(result.Content);
        Assert.All(result.Sections, s => Assert.Null(s.Content));
        var first = result.Sections[0];
        Assert.Equal(3, first.Elements.Count);
        var text = Assert.IsType<ExtractionDocumentTextElement>(first.Elements[0]);
        Assert.Equal("# Invoice Ω\nOnly prose, not cell text.", text.Text);
        Assert.Equal(text.Text, Assert.IsType<TextContent>(text.Content).Text);
        var table = Assert.IsType<ExtractionDocumentTableElement>(first.Elements[1]);
        Assert.Equal(2, table.Cells.RowCount);
        Assert.Equal(2, table.Cells.ColumnCount);
        Assert.Null(table.Content);
        var header = table.Cells[0, 0]!;
        Assert.Equal((0, 0, 1, 2, "columnHeader"), (header.RowIndex, header.ColumnIndex, header.RowSpan, header.ColumnSpan, header.Role));
        Assert.Equal("Produit", Assert.IsType<TextContent>(Assert.IsType<ExtractionDocumentTextElement>(header.Content).Content).Text);
        Assert.Null(table.Cells[0, 1]);
        var body = table.Cells[1, 0]!;
        Assert.Equal((1, 0, 1, 1), (body.RowIndex, body.ColumnIndex, body.RowSpan, body.ColumnSpan));
        Assert.Equal("Thé", Assert.IsType<ExtractionDocumentTextElement>(body.Content).Text);
        Assert.Null(body.Role);
        var contentless = table.Cells[1, 1]!;
        Assert.Null(contentless.Content);
        Assert.Equal("stub", contentless.Role);
        Assert.Equal((1, 1, 1, 1), (contentless.RowIndex, contentless.ColumnIndex, contentless.RowSpan, contentless.ColumnSpan));
        var figure = Assert.IsType<ExtractionDocumentImageElement>(first.Elements[2]);
        Assert.Equal("Figure 1: Revenue €", figure.Text);
        Assert.Equal("Figure 1: Revenue €", figure.AlternativeText);
        Assert.Null(figure.Content);
        Assert.Null(figure.Source);
        var second = result.Sections[1];
        Assert.Equal("Second unnumbered panel", Assert.IsType<TextContent>(second.Elements[0].Content).Text);
        var literalTable = Assert.IsType<ExtractionDocumentTableElement>(second.Elements[1]);
        Assert.Equal((0, 0), (literalTable.Cells.RowCount, literalTable.Cells.ColumnCount));
        Assert.Equal("| literal | table |\n|---|---|", Assert.IsType<TextContent>(literalTable.Content).Text);
        Assert.Empty(literalTable.Cells);
        var emptyFigure = Assert.IsType<ExtractionDocumentImageElement>(second.Elements[2]);
        Assert.Null(emptyFigure.Text);
        Assert.Null(emptyFigure.Content);
        Assert.Null(emptyFigure.AlternativeText);
        Assert.Equal(6, result.EnumerateContent().Count()); // Cells are addressed, not flattened.
        for (int i = 0; i < 2; i++)
        {
            var reference = Assert.Single(result.Sections[i].Sources);
            Assert.Same(result.SourcePages[i], reference.Page);
            Assert.Null(reference.Page.Dimensions);
            Assert.Null(reference.Page.CoordinateUnit);
            Assert.Null(reference.Page.CoordinateOrigin);
            Assert.Empty(reference.Polygon);
            Assert.Same(explicitRaw ? raw : response, reference.GetRawRepresentation());
            Assert.All(result.Sections[i].Elements, e => Assert.Same(reference, Assert.Single(e.Sources)));
        }

        Assert.All(table.Cells.Where(c => c is not null), cell =>
        {
            Assert.Same(first.Sources[0], Assert.Single(cell!.Sources));
            if (cell.Content is not null)
            {
                Assert.Same(first.Sources[0], Assert.Single(cell.Content.Sources));
            }
        });
        var output = Assert.Single(result.SourceOutputs);
        Assert.Equal("vision-llm", output.ProviderName);
        Assert.Equal("actual-vision-model", output.ModelId);
        Assert.Same(explicitRaw ? raw : response, output.GetRawRepresentation());
        Assert.All(result.SourcePages, p => Assert.Same(output, p.SourceOutput));
        Assert.Equal(capture == true ? "Unavailable" : "Disabled", output.ContentStatus.ToString());
        Assert.Null(output.GetRawContent()); // JSON text/native object are not an exact provider response body.
        using var preservedResponse = JsonDocument.Parse(Assert.IsType<ChatResponse>(
            explicitRaw ? response : output.GetRawRepresentation()).Text);
        JsonElement preservedNative = explicitRaw ? Assert.IsType<JsonElement>(output.GetRawRepresentation()) : preservedResponse.RootElement;
        Assert.Equal("fr", preservedNative.GetProperty("pages")[0].GetProperty("language").GetString());
        Assert.Equal(0.875, preservedNative.GetProperty("pages")[0].GetProperty("confidence").GetDouble());
        Assert.Equal(Valid, response.Text);
        var request = Assert.Single(chat.Requests);
        Assert.Equal(capture is null ? null : "requested-structured-route", request.Options!.ModelId);
        var schema = Assert.IsType<ChatResponseFormatJson>(request.Options.ResponseFormat).Schema!.Value;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("pages", schema.GetProperty("required")[0].GetString());
        var message = Assert.Single(request.Messages);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal(2, message.Contents.Count);
        Assert.Contains("JSON object with a pages array", Assert.IsType<TextContent>(message.Contents[0]).Text);
        var image = Assert.IsType<DataContent>(message.Contents[1]);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 11, 22, 33 }, image.Data.ToArray());
        client.Dispose();
        input.AssertStillReadable(137);
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Theory]
    [InlineData("""{"pages":[]}""", 0, null)]
    [InlineData("""{"pages":[{}]}""", 1, null)]
    [InlineData("""{"pages":[{"index":null,"markdown":null,"language":null,"confidence":null}]}""", 1, null)]
    [InlineData("""{"pages":[{"index":0,"markdown":"","language":"und","confidence":0}]}""", 1, 1)]
    [InlineData("""{"pages":[{"index":9,"confidence":1}]}""", 1, 10)]
    public async Task ExtractAsync_OptionalFieldsAndBoundaryIndices_DoNotInventFramesOrText(string json, int count, int? number)
    {
        var response = RecordingChatClient.Response(json, model: null);
        var chat = new RecordingChatClient(response);
        using var client = new VisionLlmOcrClient(chat, prompt: "Use the strict schema.", useStructuredOutput: true);
        using var input = new BorrowedInput([1, 2, 3]);
        var result = await client.ExtractAsync(input, "named-image.png", "image/png", new() { ExtractorName = "not-returned-model" });
        Assert.Equal(count, result.SourcePages.Count);
        Assert.Equal(count, result.Sections.Count);
        Assert.All(result.SourcePages, p => Assert.Equal(number, p.PageNumber));
        Assert.Empty(result.EnumerateContent());
        Assert.Null(Assert.Single(result.SourceOutputs).ModelId);
        Assert.Same(response, result.SourceOutputs[0].GetRawRepresentation());
        Assert.Equal("Use the strict schema.", Assert.IsType<TextContent>(Assert.Single(chat.Requests).Messages[0].Contents[0]).Text);
        Assert.IsType<ChatResponseFormatJson>(chat.Requests[0].Options!.ResponseFormat);
        input.AssertStillReadable(1);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("```json\n{\"pages\":[]}\n```")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"pages":null}""")]
    [InlineData("""{"pages":{}}""")]
    [InlineData("""{"pages":[null]}""")]
    [InlineData("""{"pages":[{"index":"0"}]}""")]
    [InlineData("""{"pages":[{"markdown":3}]}""")]
    [InlineData("""{"pages":[{"confidence":"0.5"}]}""")]
    [InlineData("""{"pages":[{"tables":{}}]}""")]
    [InlineData("""{"pages":[{"figures":"bad"}]}""")]
    [InlineData("""{"pages":[{"figures":[{"caption":4}]}]}""")]
    [InlineData("""{"pages":[{"tables":[{"columnCount":2}]}]}""")]
    [InlineData("""{"pages":[{"tables":[{"rowCount":2,"columnCount":"2"}]}]}""")]
    [InlineData("""{"pages":[{"tables":[{"rowCount":2,"columnCount":2,"cells":{}}]}]}""")]
    [InlineData("""{"pages":[{"tables":[{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":0}]}]}]}""")]
    public Task ExtractAsync_MalformedJsonOrFieldTypes_RejectsWithoutFreeformFallback(string json)
        => AssertStrictRejectionAsync(json);

    [Theory]
    [InlineData("""{"pages":[{"confidence":-0.001}]}""")]
    [InlineData("""{"pages":[{"confidence":1.001}]}""")]
    [InlineData("""{"pages":[{"index":-1}]}""")]
    [InlineData("""{"pages":[{"index":2147483647}]}""")]
    [InlineData("""{"pages":[{"index":0.5}]}""")]
    [InlineData("""{"pages":[{"index":2},{"index":2}]}""")]
    public Task ExtractAsync_InvalidConfidenceOrIndices_RejectsWithoutFreeformFallback(string json)
        => AssertStrictRejectionAsync(json);

    [Theory]
    [InlineData("""{"rowCount":-1,"columnCount":2}""")]
    [InlineData("""{"rowCount":2,"columnCount":-1}""")]
    [InlineData("""{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":2,"columnIndex":0}]}""")]
    [InlineData("""{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":0,"columnIndex":-1}]}""")]
    [InlineData("""{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":0,"columnIndex":0,"rowSpan":0}]}""")]
    [InlineData("""{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":0,"columnIndex":0,"columnSpan":-1}]}""")]
    [InlineData("""{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":1,"columnIndex":0,"rowSpan":2}]}""")]
    [InlineData("""{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":0,"columnIndex":1,"columnSpan":2}]}""")]
    [InlineData("""{"rowCount":2,"columnCount":2,"cells":[{"rowIndex":0,"columnIndex":0,"columnSpan":2},{"rowIndex":0,"columnIndex":1}]}""")]
    public Task ExtractAsync_InvalidGridOrSpans_RejectsWithoutFreeformFallback(string table)
        => AssertStrictRejectionAsync($"{{\"pages\":[{{\"tables\":[{table}]}}]}}");

    [Theory]
    [InlineData("unknown-root-key", """{"pages":[],"unexpected":true}""")]
    [InlineData("numeric-language", """{"pages":[{"language":42}]}""")]
    [InlineData("null-tables", """{"pages":[{"tables":null}]}""")]
    [InlineData("null-figures", """{"pages":[{"figures":null}]}""")]
    [InlineData("null-cells", """{"pages":[{"tables":[{"rowCount":1,"columnCount":1,"cells":null}]}]}""")]
    [InlineData("duplicate-anchor", """{"pages":[{"tables":[{"rowCount":1,"columnCount":1,"cells":[{"rowIndex":0,"columnIndex":0,"text":"A"},{"rowIndex":0,"columnIndex":0,"text":"B"}]}]}]}""")]
    public async Task ExtractAsync_StrictShapeAndConflictingAnchors_RejectsWithoutFreeformFallback(string scenario, string json)
        => Assert.IsType<InvalidDataException>(await AssertStrictRejectionAsync(json, scenario));

    private static async Task<Exception> AssertStrictRejectionAsync(string json, string identifier = "bad-shape.png")
    {
        var chat = new RecordingChatClient(RecordingChatClient.Response(json), RecordingChatClient.Response("forbidden freeform fallback"));
        using var input = new BorrowedInput([7, 8, 9]);
        var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        Exception? error = await Record.ExceptionAsync(() => client.ExtractAsync(input, identifier, "image/png"));
        client.Dispose();
        input.AssertStillReadable(7);
        Assert.Equal(0, chat.DisposeCalls);
        Assert.IsType<ChatResponseFormatJson>(Assert.Single(chat.Requests).Options!.ResponseFormat);
        Assert.NotNull(error);
        Assert.True(error is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException
            or KeyNotFoundException or FormatException,
            $"Expected a parsing/shape rejection, got {error.GetType().Name}: {error.Message}");
        return error;
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("failure")]
    public async Task ExtractAsync_ChatFailure_PropagatesPrimaryErrorWithoutSchemaFallbackAndBorrowsResources(string kind)
    {
        Exception failure = kind == "capability"
            ? new NotSupportedException("JSON schema response format unsupported")
            : new InvalidOperationException("deterministic structured failure");
        var chat = new RecordingChatClient(failure, RecordingChatClient.Response("""{"pages":[]}"""));
        using var input = new BorrowedInput([4, 5, 6]);
        var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        Assert.Same(failure, await Record.ExceptionAsync(() => client.ExtractAsync(input, "failure.png", "image/png")));
        Assert.IsType<ChatResponseFormatJson>(Assert.Single(chat.Requests).Options!.ResponseFormat);
        client.Dispose();
        client.Dispose();
        input.AssertStillReadable(4);
        Assert.Equal(0, chat.DisposeCalls);
        Assert.Equal("""{"pages":[]}""", (await chat.GetResponseAsync([])).Text);
    }

    [Theory]
    [InlineData("before-copy", 0)]
    [InlineData("during-copy", 0)]
    [InlineData("during-chat", 1)]
    public async Task ExtractAsync_Cancellation_PreservesTokenAndBorrowedResourcesWithoutFallback(string stage, int calls)
    {
        using var cancellation = new CancellationTokenSource();
        var chat = new RecordingChatClient(RecordingChatClient.Response("""{"pages":[]}"""))
        {
            OnRequest = stage == "during-chat" ? _ => cancellation.Cancel() : null,
        };
        using var input = new BorrowedInput([8, 9, 10], stage == "during-copy" ? cancellation : null);
        var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        if (stage == "before-copy")
        {
            cancellation.Cancel();
        }

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ExtractAsync(input, "cancel.png", "image/png", cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(calls, chat.Requests.Count);
        if (calls == 1)
        {
            Assert.Equal(cancellation.Token, chat.Requests[0].Token);
            Assert.IsType<ChatResponseFormatJson>(chat.Requests[0].Options!.ResponseFormat);
        }

        client.Dispose();
        input.AssertStillReadable(8);
        Assert.Equal(0, chat.DisposeCalls);
        Assert.Equal("""{"pages":[]}""", (await chat.GetResponseAsync([])).Text);
    }

    [Fact]
    public async Task ExtractAsync_LocalPdfStructured_PreservesNativeImageOrderOriginalFramesAndKnownIndexPrompt()
    {
        var chat = new RecordingChatClient(
            RecordingChatClient.Response("""{"pages":[{"index":0,"markdown":"First"}]}"""),
            RecordingChatClient.Response("""{"pages":[{"markdown":"Blank source page"}]}"""),
            RecordingChatClient.Response("""{"pages":[{"index":2,"figures":[{"caption":"Red square"}]}]}"""));
        using var input = new BorrowedInput(PdfFixture.Create());
        using var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        var result = await client.ExtractAsync(input, "native.pdf", "application/pdf");
        Assert.Equal(3, chat.Requests.Count);
        Assert.Equal(new int?[] { 1, 2, 3 }, result.SourcePages.Select(p => p.PageNumber));
        Assert.Equal(new[] { (300d, 400d), (500d, 600d), (700d, 800d) },
            result.SourcePages.Select(p => (p.Dimensions!.Value.Width, p.Dimensions.Value.Height)));
        Assert.Equal("First", Assert.IsType<TextContent>(result.Sections[0].Elements[0].Content).Text);
        Assert.Equal("Blank source page", Assert.IsType<TextContent>(result.Sections[1].Elements[0].Content).Text);
        Assert.Equal("Red square", Assert.IsType<ExtractionDocumentImageElement>(result.Sections[2].Elements[0]).AlternativeText);
        for (int i = 0; i < 3; i++)
        {
            var page = result.SourcePages[i];
            Assert.Same(result.SourceOutputs[i], page.SourceOutput);
            Assert.Same(page, Assert.Single(result.Sections[i].Elements[0].Sources).Page);
            Assert.Equal(ExtractionDocumentCoordinateUnit.Point, page.CoordinateUnit);
            Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
            var request = chat.Requests[i];
            Assert.IsType<ChatResponseFormatJson>(request.Options!.ResponseFormat);
            Assert.Contains($"original page index {i}. Return exactly one page.", Assert.IsType<TextContent>(request.Messages[0].Contents[0]).Text);
            var image = Assert.IsType<DataContent>(request.Messages[0].Contents[1]);
            Assert.Equal("image/png", image.MediaType);
            using var bitmap = SKBitmap.Decode(image.Data.ToArray());
            Assert.Equal((int)page.Dimensions!.Value.Width * 2, bitmap.Width);
            Assert.Equal((int)page.Dimensions.Value.Height * 2, bitmap.Height);
        }

        input.AssertStillReadable((byte)'%');
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Theory]
    [InlineData("""{"pages":[]}""")]
    [InlineData("""{"pages":[{},{}]}""")]
    [InlineData("""{"pages":[{"index":1}]}""")]
    public async Task ExtractAsync_LocalPdfInconsistentIndexOrCount_RejectsWithoutFallback(string json)
    {
        var chat = new RecordingChatClient(RecordingChatClient.Response(json), RecordingChatClient.Response("forbidden fallback"));
        using var input = new BorrowedInput(PdfFixture.Create());
        using var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(input, "native.pdf", "application/pdf"));
        Assert.IsType<ChatResponseFormatJson>(Assert.Single(chat.Requests).Options!.ResponseFormat);
        Assert.Equal("image/png", Assert.IsType<DataContent>(chat.Requests[0].Messages[0].Contents[1]).MediaType);
        input.AssertStillReadable((byte)'%');
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public async Task GetServiceAndDispose_PreserveBorrowedChatAndValidateStructuredConstructor()
    {
        Assert.Throws<ArgumentNullException>(() => new VisionLlmOcrClient(null!, useStructuredOutput: true));
        var chat = new RecordingChatClient(RecordingChatClient.Response("""{"pages":[{"markdown":"still usable"}]}"""));
        var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        Assert.Same(client, client.GetService(typeof(IDocumentExtractionClient)));
        Assert.Same(client, client.GetService(typeof(VisionLlmOcrClient)));
        Assert.Same(chat, client.GetService(typeof(IChatClient)));
        Assert.Null(client.GetService(typeof(string)));
        Assert.Null(client.GetService(typeof(IChatClient), "key"));
        Assert.Equal((typeof(IChatClient), (object?)"key"), chat.ServiceRequests[^1]);
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
        client.Dispose();
        client.Dispose();
        using var input = new BorrowedInput([2, 3, 4]);
        var result = await client.ExtractAsync(input, "after-dispose.png", "image/png");
        Assert.Equal("still usable", Assert.IsType<TextContent>(Assert.Single(result.EnumerateContent()).Content).Text);
        Assert.IsType<ChatResponseFormatJson>(Assert.Single(chat.Requests).Options!.ResponseFormat);
        input.AssertStillReadable(2);
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public async Task ExtractAsync_ValidGridBoundarySpansAndConfidenceEndpoints_PreserveAnchorsWithoutDuplicatingText()
    {
        const string json =
            """
            {"pages":[{"confidence":0,"language":"en","tables":[{"rowCount":2,"columnCount":3,
              "cells":[{"rowIndex":0,"columnIndex":0,"text":"North","rowSpan":2,"role":"rowHeader"},
                       {"rowIndex":0,"columnIndex":1,"text":"Quarter","columnSpan":2,"role":"columnHeader"},
                       {"rowIndex":1,"columnIndex":1,"text":"Q1"},
                       {"rowIndex":1,"columnIndex":2,"text":"€125.50"}]}]},
              {"index":0,"confidence":1,"markdown":"Verified"}]}
            """;
        var chat = new RecordingChatClient(RecordingChatClient.Response(json));
        using var client = new VisionLlmOcrClient(chat, useStructuredOutput: true);
        using var input = new BorrowedInput([6, 7, 8]);
        var result = await client.ExtractAsync(input, "boundaries.png", "image/png");
        var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(result.Sections[0].Elements));
        Assert.Equal((2, 3), (table.Cells.RowCount, table.Cells.ColumnCount));
        Assert.Null(table.Content);
        Assert.Equal((0, 0, 2, 1, "rowHeader"), (table.Cells[0, 0]!.RowIndex, table.Cells[0, 0]!.ColumnIndex,
            table.Cells[0, 0]!.RowSpan, table.Cells[0, 0]!.ColumnSpan, table.Cells[0, 0]!.Role));
        Assert.Equal("North", Assert.IsType<ExtractionDocumentTextElement>(table.Cells[0, 0]!.Content).Text);
        Assert.Null(table.Cells[1, 0]);
        Assert.Null(table.Cells[0, 2]);
        Assert.Equal("€125.50", Assert.IsType<TextContent>(table.Cells[1, 2]!.Content!.Content).Text);
        Assert.Equal((1, 2, 1, 1), (table.Cells[1, 2]!.RowIndex, table.Cells[1, 2]!.ColumnIndex,
            table.Cells[1, 2]!.RowSpan, table.Cells[1, 2]!.ColumnSpan));
        Assert.Equal(new int?[] { null, 1 }, result.SourcePages.Select(p => p.PageNumber));
        Assert.Equal(2, result.EnumerateContent().Count());
        Assert.Same(result.SourcePages[0], Assert.Single(table.Cells[1, 2]!.Sources).Page);
        Assert.Equal("Verified", Assert.IsType<TextContent>(result.Sections[1].Elements[0].Content).Text);
        Assert.IsType<ChatResponseFormatJson>(Assert.Single(chat.Requests).Options!.ResponseFormat);
        input.AssertStillReadable(6);
    }
}
