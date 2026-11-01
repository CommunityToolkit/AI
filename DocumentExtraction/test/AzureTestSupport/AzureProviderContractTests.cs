using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
#if CONTENT_UNDERSTANDING
using SdkClient = Azure.AI.ContentUnderstanding.ContentUnderstandingClient;
using SdkOptions = Azure.AI.ContentUnderstanding.ContentUnderstandingClientOptions;
using Adapter = CommunityToolkit.DocumentExtraction.AzureContentUnderstanding.ContentUnderstandingClient;
using NativeResult = Azure.AI.ContentUnderstanding.AnalysisResult;
using NativeContent = Azure.AI.ContentUnderstanding.DocumentContent;
#else
using SdkClient = Azure.AI.DocumentIntelligence.DocumentIntelligenceClient;
using SdkOptions = Azure.AI.DocumentIntelligence.DocumentIntelligenceClientOptions;
using Adapter = CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence.AzureDocumentIntelligenceClient;
using NativeResult = Azure.AI.DocumentIntelligence.AnalyzeResult;
#endif

namespace CommunityToolkit.DocumentExtraction.Tests;

#if CONTENT_UNDERSTANDING
public sealed class ContentUnderstandingClientTests
#else
public sealed class AzureDocumentIntelligenceClientTests
#endif
{
    private static Adapter Create(OfflineTransport transport, bool borrowed = false, bool images = false,
        string? selector = null, FixedCredential? credential = null)
    {
        var options = new SdkOptions { Transport = transport };
        options.Retry.MaxRetries = 0;
        var endpoint = new Uri("https://offline.test");
        credential ??= new FixedCredential();
#if CONTENT_UNDERSTANDING
        return borrowed
            ? new Adapter(new SdkClient(endpoint, credential, options), selector ?? Adapter.DefaultAnalyzerId)
            : new Adapter(endpoint, credential, selector ?? Adapter.DefaultAnalyzerId, options);
#else
        return borrowed
            ? new Adapter(new SdkClient(endpoint, credential, options), selector ?? "prebuilt-layout", images)
            : new Adapter(endpoint, credential, selector ?? "prebuilt-layout", images, options);
#endif
    }

    private static OfflineTransport Replay(byte[] bytes, params TrackedResponse[] tail)
        => new([TrackedResponse.Accepted(), new TrackedResponse(bytes), .. tail]);

    private static Task<ExtractionDocument> Extract(Adapter client, Stream input, DocumentExtractionOptions? options = null,
        CancellationToken token = default)
        => client.ExtractAsync(input, "invoice.pdf", "application/pdf", options, token);

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "configured")]
    [InlineData(true, "configured")]
    public async Task Constructors_DefaultAndExplicitSelector_UseRealSdkWithFakeCredential(bool borrowed, string? selector)
    {
        var transport = Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()));
        var credential = new FixedCredential();
        using var client = Create(transport, borrowed, selector: selector, credential: credential);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(RequestMethod.Post, transport.Requests[0].Method);
        Assert.Contains(selector ?? (AzureWireFixture.IsContentUnderstanding ? "prebuilt-document" : "prebuilt-layout"),
            transport.Requests[0].Uri.AbsolutePath);
        Assert.True(credential.Calls > 0);
        Assert.IsType<NativeResult>(Assert.Single(result.SourceOutputs).GetRawRepresentation());
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
    }

    [Fact]
    public async Task ExtractAsync_SelectorAndProviderOptions_ReachReplayRequest()
    {
        var transport = Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()));
        using var client = Create(transport, selector: "configured");
        using var input = new MemoryStream([1, 2, 3]);
        var options = new DocumentExtractionOptions { ExtractorName = "requested", CaptureRawContent = true };
#if CONTENT_UNDERSTANDING
        options.AdditionalProperties["processingLocation"] = "DataZone";
#else
        options.AdditionalProperties["locale"] = "fr-FR";
        options.AdditionalProperties["pages"] = "3";
        options.AdditionalProperties["features"] = new[] { "languages", "barcodes" };
#endif
        var result = await Extract(client, input, options);
        string request = Uri.UnescapeDataString(transport.Requests[0].Uri.AbsoluteUri);
        Assert.Contains("requested", request);
#if CONTENT_UNDERSTANDING
        Assert.Contains("processingLocation=DataZone", request);
        Assert.Null(Assert.Single(result.SourceOutputs).ModelId);
        Assert.Equal("DataZone", options.AdditionalProperties["processingLocation"]);
#else
        Assert.Contains("locale=fr-FR", request);
        Assert.Contains("pages=3", request);
        Assert.Contains("languages", request);
        Assert.Contains("barcodes", request);
        Assert.Equal("native-model", Assert.Single(result.SourceOutputs).ModelId);
        Assert.Equal("fr-FR", options.AdditionalProperties["locale"]);
#endif
        Assert.Equal("requested", options.ExtractorName);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_AcceptedRunningSucceeded_CaptureMatrixPreservesNativeIdentityAndExactRawBody(bool? capture)
    {
        byte[] bytes = AzureWireFixture.Bytes(AzureWireFixture.Create());
        var running = new TrackedResponse(Encoding.UTF8.GetBytes("""{"status":"running","id":"receipt"}"""));
        var final = new TrackedResponse(bytes);
        var transport = new OfflineTransport(TrackedResponse.Accepted(), running, final);
        var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input,
            capture.HasValue ? new DocumentExtractionOptions { CaptureRawContent = capture.Value } : null);
        client.Dispose();
        var output = Assert.Single(result.SourceOutputs);
        Assert.Equal(capture == true ? "Retained" : "Disabled", output.ContentStatus.ToString());
        Assert.Equal(AzureWireFixture.IsContentUnderstanding ? "azure-content-understanding" : "azure-document-intelligence",
            output.ProviderName);
        if (capture == true)
        {
            Assert.Equal(bytes, output.GetRawContent()!.Content.ToArray());
            Assert.Equal("application/json", output.GetRawContent()!.MediaType);
        }
        else
        {
            Assert.Null(output.GetRawContent());
        }

        var native = Assert.IsType<NativeResult>(output.GetRawRepresentation());
#if CONTENT_UNDERSTANDING
        var content = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
        object page = content.Pages[0];
        object paragraph = content.Paragraphs[0];
        Assert.Null(output.ModelId);
#else
        object page = native.Pages[0];
        object paragraph = native.Paragraphs[0];
        Assert.Equal(native.ModelId, output.ModelId);
#endif
        Assert.Same(page, result.Sections[0].Sources[0].GetRawRepresentation());
        var text = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTextElement>());
        Assert.Same(paragraph, text.Sources[0].GetRawRepresentation());
        Assert.Equal("Invoice heading", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Equal("heading", text.Role);
        Assert.Equal(3, transport.Requests.Count);
        Assert.All(transport.Requests.Skip(1), request => Assert.Equal(RequestMethod.Get, request.Method));
        Assert.Equal(2, final.DisposeCount);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Fact]
    public async Task ExtractAsync_MultiVertexTableCellsAndFigures_PreserveOrderedGeometryAndNativePayload()
    {
        var transport = Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var frame = Assert.Single(result.SourcePages);
        Assert.Equal(3, frame.PageNumber);
        Assert.Equal(300, frame.Dimensions!.Value.Width);
        Assert.Equal(400, frame.Dimensions.Value.Height);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, frame.CoordinateOrigin);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Pixel, frame.CoordinateUnit);
        Assert.Same(Assert.Single(result.SourceOutputs), frame.SourceOutput);
        var text = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTextElement>());
        Assert.Equal(AzureWireFixture.Points, text.Sources[0].Polygon.Select(point => (point.X, point.Y)));
#if CONTENT_UNDERSTANDING
        Assert.Equal(3, text.Sources.Count);
        Assert.Empty(text.Sources[1].Polygon);
        Assert.Equal(AzureWireFixture.Points, text.Sources[2].Polygon.Select(point => (point.X, point.Y)));
        Assert.Same(text.Sources[0].GetRawRepresentation(), text.Sources[2].GetRawRepresentation());
#else
        Assert.Single(text.Sources);
#endif
        Assert.All(text.Sources, source => Assert.Same(frame, source.Page));
        var table = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTableElement>());
        Assert.Equal(2, table.Cells.RowCount);
        Assert.Equal(3, table.Cells.ColumnCount);
        var cell = table.Cells[0, 0]!;
        Assert.Equal(2, cell.RowSpan);
        Assert.Equal(2, cell.ColumnSpan);
        Assert.Equal(0, cell.RowIndex);
        Assert.Equal(0, cell.ColumnIndex);
        Assert.Equal("columnHeader", cell.Role);
        Assert.Null(table.Cells[0, 2]);
        var cellText = Assert.IsType<ExtractionDocumentTextElement>(cell.Content);
        Assert.Equal("Total amount", Assert.IsType<TextContent>(cellText.Content).Text);
        Assert.Equal(AzureWireFixture.Points, Assert.Single(cell.Sources).Polygon.Select(point => (point.X, point.Y)));
        Assert.Same(cell.Sources[0], Assert.Single(cellText.Sources));
        var figure = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentImageElement>());
        Assert.Equal("Revenue chart", figure.AlternativeText);
        Assert.Null(figure.Content);
        Assert.Same(frame, Assert.Single(figure.Sources).Page);
        Assert.Equal(AzureWireFixture.Points, figure.Sources[0].Polygon.Select(point => (point.X, point.Y)));
        var native = Assert.IsType<NativeResult>(Assert.Single(result.SourceOutputs).GetRawRepresentation());
#if CONTENT_UNDERSTANDING
        var content = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
        Assert.Same(content.Tables[0], table.Sources[0].GetRawRepresentation());
        Assert.Same(content.Tables[0].Cells[0], cell.Sources[0].GetRawRepresentation());
        Assert.Same(content.Figures[0], figure.Sources[0].GetRawRepresentation());
        Assert.Equal("Revenue chart", figure.Text);
#else
        Assert.Same(native.Tables[0], table.Sources[0].GetRawRepresentation());
        Assert.Same(native.Tables[0].Cells[0], cell.Sources[0].GetRawRepresentation());
        Assert.Same(native.Figures[0], figure.Sources[0].GetRawRepresentation());
#endif
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExtractAsync_NullVersusEmptyNativeCellContent_PreservesPayloadSourcesAndIdentity(string? text)
    {
        var root = AzureWireFixture.Create();
        var wireCell = AzureWireFixture.Content(root)["tables"]![0]!["cells"]![0]!;
        if (text is null)
        {
            wireCell.AsObject().Remove("content");
        }
        else
        {
            wireCell["content"] = text;
        }

        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var table = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTableElement>());
        var cell = table.Cells[0, 0]!;
        Assert.Equal((0, 0, 2, 2, "columnHeader"),
            (cell.RowIndex, cell.ColumnIndex, cell.RowSpan, cell.ColumnSpan, cell.Role));
        var source = Assert.Single(cell.Sources);
        Assert.Same(Assert.Single(result.SourcePages), source.Page);
        Assert.Equal(AzureWireFixture.Points, source.Polygon.Select(point => (point.X, point.Y)));
        var native = Assert.IsType<NativeResult>(Assert.Single(result.SourceOutputs).GetRawRepresentation());
#if CONTENT_UNDERSTANDING
        var nativeCell = Assert.IsType<NativeContent>(Assert.Single(native.Contents)).Tables[0].Cells[0];
#else
        var nativeCell = native.Tables[0].Cells[0];
#endif
        Assert.Equal(text, nativeCell.Content);
        Assert.Same(nativeCell, source.GetRawRepresentation());
        if (text is null)
        {
            Assert.Null(cell.Content);
        }
        else
        {
            var element = Assert.IsType<ExtractionDocumentTextElement>(cell.Content);
            Assert.Equal("", element.Text);
            Assert.Equal("", Assert.IsType<TextContent>(element.Content).Text);
            Assert.Same(source, Assert.Single(element.Sources));
        }

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Fact]
    public async Task ExtractAsync_NestedSections_PreservesPointerOrder()
    {
        var root = AzureWireFixture.Create();
        AzureWireFixture.Content(root)["sections"] = new JsonArray(
            AzureWireFixture.Section("/paragraphs/0", "/figures/0"),
            AzureWireFixture.Section("/tables/0", "/sections/0"));
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var outer = Assert.IsType<ExtractionDocumentSection>(Assert.Single(result.Sections[0].Elements));
        Assert.Equal(2, outer.Elements.Count);
        var table = Assert.IsType<ExtractionDocumentTableElement>(outer.Elements[0]);
        var inner = Assert.IsType<ExtractionDocumentSection>(outer.Elements[1]);
        Assert.Collection(inner.Elements,
            element => Assert.Equal("Invoice heading", Assert.IsType<ExtractionDocumentTextElement>(element).Text),
            element => Assert.Equal("Revenue chart", Assert.IsType<ExtractionDocumentImageElement>(element).AlternativeText));
        Assert.Equal(new[] { typeof(ExtractionDocumentTableElement), typeof(ExtractionDocumentTextElement), typeof(ExtractionDocumentImageElement) },
            result.EnumerateContent().Select(element => element.GetType()));
        Assert.Same(table, result.EnumerateContent().First());
        Assert.Same(result.SourcePages[0], Assert.Single(inner.Sources).Page);
        var native = Assert.IsType<NativeResult>(Assert.Single(result.SourceOutputs).GetRawRepresentation());
#if CONTENT_UNDERSTANDING
        Assert.Same(Assert.IsType<NativeContent>(Assert.Single(native.Contents)).Sections[0], inner.Sources[0].GetRawRepresentation());
#else
        Assert.Same(native.Sections[0], inner.Sources[0].GetRawRepresentation());
#endif
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unsupported")]
    [InlineData("missing")]
    [InlineData("self")]
    [InlineData("cycle")]
    [InlineData("multi-parent")]
    [InlineData("duplicate")]
    public async Task ExtractAsync_MalformedOrCyclicSectionPointers_RejectsAndCleansUp(string scenario)
    {
        var root = AzureWireFixture.Create();
        JsonArray sections = scenario switch
        {
            "malformed" => new(AzureWireFixture.Section("paragraphs/0")),
            "unsupported" => new(AzureWireFixture.Section("/pages/0")),
            "missing" => new(AzureWireFixture.Section("/paragraphs/999")),
            "self" => new(AzureWireFixture.Section("/sections/0")),
            "cycle" => new(AzureWireFixture.Section("/sections/1"), AzureWireFixture.Section("/sections/0")),
            "multi-parent" => new(AzureWireFixture.Section("/paragraphs/0"), AzureWireFixture.Section("/paragraphs/0")),
            _ => new(AzureWireFixture.Section("/tables/0", "/tables/0")),
        };
        AzureWireFixture.Content(root)["sections"] = sections;
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Extract(client, input, token: guard.Token));
        Assert.Contains(scenario is "self" or "cycle" ? "cyclic" :
            scenario is "multi-parent" or "duplicate" ? "more than one" : "unsupported", error.Message);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Fact]
    public async Task ExtractAsync_NoParagraphs_UsesLinesFallback()
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["paragraphs"] = new JsonArray();
        content["tables"] = new JsonArray();
        content["figures"] = new JsonArray();
        content["pages"]![0]!["lines"] = JsonSerializer.SerializeToNode(new[]
        {
            new { content = "First line", source = "D(3,1,2,3,2,3,4,2,5,1,4)", polygon = AzureWireFixture.Coordinates,
                spans = new[] { new { offset = 0, length = 10 } } },
            new { content = "Second line", source = "D(3)", polygon = Array.Empty<double>(),
                spans = new[] { new { offset = 11, length = 11 } } },
        });
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var lines = result.EnumerateContent().Cast<ExtractionDocumentTextElement>().ToArray();
        Assert.Equal(new[] { "First line", "Second line" }, lines.Select(line => Assert.IsType<TextContent>(line.Content).Text));
        Assert.All(lines, line => Assert.Same(result.SourcePages[0], Assert.Single(line.Sources).Page));
        Assert.Equal(AzureWireFixture.Points, lines[0].Sources[0].Polygon.Select(point => (point.X, point.Y)));
        Assert.Empty(lines[1].Sources[0].Polygon);
        var native = Assert.IsType<NativeResult>(Assert.Single(result.SourceOutputs).GetRawRepresentation());
#if CONTENT_UNDERSTANDING
        var page = Assert.IsType<NativeContent>(Assert.Single(native.Contents)).Pages[0];
#else
        var page = native.Pages[0];
#endif
        Assert.Same(page.Lines[0], lines[0].Sources[0].GetRawRepresentation());
        Assert.Equal(2, result.Sections[0].Elements.Count);
    }

    [Fact]
    public async Task ExtractAsync_UnknownSourceParagraph_PreservesTextWithoutFabricatedFrame()
    {
        var root = AzureWireFixture.Create();
        var paragraph = AzureWireFixture.Content(root)["paragraphs"]![0]!;
#if CONTENT_UNDERSTANDING
        paragraph["source"] = null;
#else
        paragraph["boundingRegions"] = new JsonArray();
#endif
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var text = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTextElement>());
        Assert.Equal("Invoice heading", text.Text);
        Assert.Equal("Invoice heading", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Empty(text.Sources);
        Assert.Single(result.SourcePages);
        var unlocated = Assert.Single(result.Sections, section => section.Sources.Count == 0);
        Assert.Same(text, Assert.Single(unlocated.Elements));
        Assert.DoesNotContain(text, result.Sections[0].Elements);
    }

    [Theory]
    [InlineData("pre-copy")]
    [InlineData("polling")]
    [InlineData("mapping")]
    public async Task ExtractAsync_PreCopyPollingOrMappingCancellation_PreservesCancellationAndBorrowedStream(string stage)
    {
        var final = new TrackedResponse(AzureWireFixture.Bytes(AzureWireFixture.Create()));
        var transport = new OfflineTransport(TrackedResponse.Accepted(), final);
        using var cancellation = new CancellationTokenSource();
        if (stage == "pre-copy")
        {
            cancellation.Cancel();
        }
        else
        {
            transport.AfterResponse = (_, count) =>
            {
                if (count == (stage == "polling" ? 1 : 2))
                {
                    cancellation.Cancel();
                }
            };
        }

        var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Extract(client, input, token: cancellation.Token));
        client.Dispose();
        client.Dispose();
        Assert.Equal(stage == "pre-copy" ? 0 : stage == "polling" ? 1 : 2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.True(request.Token.CanBeCanceled));
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
        if (stage == "mapping")
        {
            Assert.True(final.DisposeCount > 0);
        }
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("http")]
    public async Task ExtractAsync_FailedOperationOrHttpError_PropagatesFailure(string scenario)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""{"status":"failed","error":{"code":"OfflineFailure","message":"analysis failed"}}""");
        var failure = new TrackedResponse(bytes, scenario == "http" ? 400 : 200);
        var transport = new OfflineTransport(TrackedResponse.Accepted(), failure);
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var error = await Assert.ThrowsAsync<RequestFailedException>(() => Extract(client, input));
        Assert.Contains("OfflineFailure", error.Message);
        Assert.Equal(2, transport.Requests.Count);
        Assert.True(failure.DisposeCount > 0);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Theory]
    [InlineData("unknown-page")]
    [InlineData("bad-unit")]
    [InlineData("bad-cell")]
    public async Task ExtractAsync_MappingFailure_DisposesFinalResponseWithoutMaskingFailure(string failure)
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        if (failure == "bad-unit")
        {
#if CONTENT_UNDERSTANDING
            content["unit"] = "unsupported";
#else
            content["pages"]![0]!["unit"] = "unsupported";
#endif
        }
        else if (failure == "bad-cell")
        {
            content["tables"]![0]!["cells"]![0]!["rowIndex"] = 8;
        }
        else
        {
#if CONTENT_UNDERSTANDING
            content["figures"]![0]!["source"] = "D(99)";
#else
            content["figures"]![0]!["boundingRegions"]![0]!["pageNumber"] = 99;
#endif
        }

        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        Exception? error = await Record.ExceptionAsync(() => Extract(client, input));
        Assert.NotNull(error);
        if (failure == "bad-unit")
        {
            Assert.IsType<NotSupportedException>(error);
            Assert.Contains("unsupported", error.Message);
        }
        else if (failure == "bad-cell")
        {
            Assert.IsType<ArgumentOutOfRangeException>(error);
        }
        else
        {
#if CONTENT_UNDERSTANDING
            Assert.IsType<InvalidDataException>(error);
#else
            Assert.IsType<KeyNotFoundException>(error);
#endif
        }
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        Assert.Equal(2, transport.Requests.Count);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Fact]
    public async Task Dispose_RepeatedCalls_PreservesBorrowedSdkClientAndInput()
    {
        byte[] bytes = AzureWireFixture.Bytes(AzureWireFixture.Create());
        var transport = new OfflineTransport(TrackedResponse.Accepted(), new TrackedResponse(bytes),
            TrackedResponse.Accepted(), new TrackedResponse(bytes));
        var client = Create(transport, borrowed: true);
        var sdk = Assert.IsType<SdkClient>(client.GetService(typeof(SdkClient)));
        using var input = new MemoryStream([1, 2, 3]);
        Assert.Equal(3, (await Extract(client, input)).EnumerateContent().Count());
        client.Dispose();
        client.Dispose();
        input.Position = 0;
        using var second = new Adapter(sdk);
        Assert.Equal(3, (await Extract(second, input)).EnumerateContent().Count());
        Assert.Same(sdk, client.GetService(typeof(SdkClient)));
        Assert.Equal(4, transport.Requests.Count);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Fact]
    public void GetService_SelfSdkUnknownAndKeyedLookup_ReturnsContractServices()
    {
        using var client = Create(new OfflineTransport(), borrowed: true);
        Assert.Same(client, client.GetService(typeof(Adapter)));
        Assert.Same(client, client.GetService(typeof(IDocumentExtractionClient)));
        Assert.IsType<SdkClient>(client.GetService(typeof(SdkClient)));
        Assert.Null(client.GetService(typeof(Stream)));
        Assert.Null(client.GetService(typeof(SdkClient), "key"));
    }

    [Theory]
    [InlineData("client")]
    [InlineData("selector")]
    [InlineData("identifier")]
    [InlineData("media")]
    [InlineData("stream")]
    [InlineData("option-case")]
    [InlineData("option-type")]
    public async Task Validation_InvalidInputOrOption_RejectsWithoutRequestAndKeepsBorrowedStream(string guard)
    {
        var transport = new OfflineTransport();
        using var input = new MemoryStream([1, 2, 3]);
        using var client = Create(transport);
        var options = new DocumentExtractionOptions();
        if (guard == "option-case")
        {
            options.AdditionalProperties["Pages"] = "3";
        }
        if (guard == "option-type")
        {
            options.AdditionalProperties[AzureWireFixture.IsContentUnderstanding ? "processingLocation" : "locale"] = 42;
        }
        Exception? error = await Record.ExceptionAsync(async () =>
        {
            if (guard == "client")
            {
                using var invalid = new Adapter((SdkClient)null!);
            }
            else if (guard == "selector")
            {
                using var invalid = Create(transport, selector: "");
            }
            else
            {
                await client.ExtractAsync(guard == "stream" ? null! : input,
                    guard == "identifier" ? "" : "id", guard == "media" ? "" : "application/pdf", options);
            }
        });
        if (guard is "stream" or "client")
        {
            Assert.Equal(guard == "stream" ? "document" : "client", Assert.IsType<ArgumentNullException>(error).ParamName);
        }
        else if (guard == "option-case")
        {
            Assert.IsType<NotSupportedException>(error);
        }
        else
        {
            var argument = Assert.IsType<ArgumentException>(error);
            Assert.Equal(guard switch
            {
                "identifier" => "identifier",
                "media" => "mediaType",
                "option-type" => "options",
                _ => AzureWireFixture.IsContentUnderstanding ? "analyzerId" : "defaultModel",
            }, argument.ParamName);
        }
        Assert.Empty(transport.Requests);
        Assert.Equal(!AzureWireFixture.IsContentUnderstanding && guard == "option-type" ? 3 : 0, input.Position);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

#if !CONTENT_UNDERSTANDING
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_FigureFlag_AlwaysMapsCaptionAndSource_BytesOnlyWhenEnabled(bool images)
    {
        byte[] imageBytes = [137, 80, 78, 71, 13, 10, 26, 10, 42];
        var figureResponse = new TrackedResponse(imageBytes);
        var transport = images
            ? Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()), figureResponse)
            : Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()));
        using var client = Create(transport, images: images);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var figure = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentImageElement>());
        Assert.Equal("Revenue chart", figure.AlternativeText);
        Assert.Same(result.SourcePages[0], Assert.Single(figure.Sources).Page);
        var native = Assert.IsType<NativeResult>(Assert.Single(result.SourceOutputs).GetRawRepresentation());
        Assert.Same(native.Figures[0], figure.Sources[0].GetRawRepresentation());
        if (images)
        {
            var data = Assert.IsType<DataContent>(figure.Content);
            Assert.Equal("image/png", data.MediaType);
            Assert.Equal(imageBytes, data.Data.ToArray());
            Assert.Contains("/figures/3.1", transport.Requests[^1].Uri.AbsolutePath);
            Assert.Equal(2, figureResponse.DisposeCount);
            Assert.Contains("output=figures", transport.Requests[0].Uri.Query);
        }
        else
        {
            Assert.Null(figure.Content);
            Assert.DoesNotContain("output=figures", transport.Requests[0].Uri.Query);
        }
        Assert.Equal(images ? 3 : 2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("read")]
    [InlineData("cancel")]
    public async Task ExtractAsync_FigureFailureOrCancellation_PreservesPrimaryOutcomeAndFinalCleanup(string scenario)
    {
        var figure = new TrackedResponse(scenario == "http"
            ? Encoding.UTF8.GetBytes("""{"error":{"code":"FigureFailure","message":"figure unavailable"}}""")
            : [1, 2, 3], scenario == "http" ? 400 : 200);
        using var cancellation = new CancellationTokenSource();
        if (scenario == "read")
        {
            figure.ContentStream = new FaultingBody([1, 2, 3], new IOException("figure read failure"), seekable: false);
        }
        var transport = Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()), figure);
        if (scenario == "cancel")
        {
            transport.AfterResponse = (_, count) =>
            {
                if (count == 3)
                {
                    cancellation.Cancel();
                }
            };
        }
        using var client = Create(transport, images: true);
        using var input = new MemoryStream([1, 2, 3]);
        Exception? error = await Record.ExceptionAsync(() => Extract(client, input, token: cancellation.Token));
        Assert.NotNull(error);
        if (scenario == "http")
        {
            Assert.IsType<RequestFailedException>(error);
            Assert.Contains("FigureFailure", error.Message);
        }
        else if (scenario == "cancel")
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        }
        else
        {
            Assert.IsType<IOException>(error);
            Assert.Equal("figure read failure", error.Message);
        }
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        Assert.True(figure.DisposeCount > 0);
        Assert.Equal(3, transport.Requests.Count);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }
#endif

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_UnlocatedParagraphWithNoPages_DoesNotFabricateFrame(bool hasText)
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["pages"] = new JsonArray();
        content["tables"] = new JsonArray();
        content["figures"] = new JsonArray();
        if (!hasText)
        {
            content["paragraphs"] = new JsonArray();
        }
        else
        {
#if CONTENT_UNDERSTANDING
            content["paragraphs"]![0]!["source"] = null;
#else
            content["paragraphs"]![0]!["boundingRegions"] = new JsonArray();
#endif
        }
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input, new DocumentExtractionOptions { CaptureRawContent = true });
        Assert.Empty(result.SourcePages);
        Assert.Equal("Retained", Assert.Single(result.SourceOutputs).ContentStatus.ToString());
        if (hasText)
        {
            var text = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(result.EnumerateContent()));
            Assert.Equal("Invoice heading", Assert.IsType<TextContent>(text.Content).Text);
            Assert.Empty(text.Sources);
            Assert.Empty(Assert.Single(result.Sections).Sources);
        }
        else
        {
            Assert.Empty(result.Sections);
            Assert.Empty(result.EnumerateContent());
        }
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
    }

    [Fact]
    public async Task ExtractAsync_ParagraphReferencesUnknownPage_RejectsWithoutFabricatingFrameAndCleansUp()
    {
        var root = AzureWireFixture.Create();
        var paragraph = AzureWireFixture.Content(root)["paragraphs"]![0]!;
#if CONTENT_UNDERSTANDING
        paragraph["source"] = "D(99)";
#else
        paragraph["boundingRegions"]![0]!["pageNumber"] = 99;
#endif
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
#if CONTENT_UNDERSTANDING
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Extract(client, input));
        Assert.Contains("unknown page 99", error.Message);
#else
        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() => Extract(client, input));
        Assert.Contains("99", error.Message);
#endif
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        Assert.Equal(2, transport.Requests.Count);
        client.Dispose();
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Fact]
    public async Task ExtractAsync_MidCopyCancellation_DoesNotSendRequestOrDisposeBorrowedStream()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CancellingCopyStream(cancellation);
        var transport = new OfflineTransport();
        var client = Create(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Extract(client, input, token: cancellation.Token));
        client.Dispose();
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Empty(transport.Requests);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    private sealed class CancellingCopyStream(CancellationTokenSource cancellation) : MemoryStream(new byte[] { 1, 2, 3 })
    {
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            destination.WriteByte(1);
            cancellation.Cancel();
            return Task.FromCanceled(cancellationToken);
        }
    }
}
