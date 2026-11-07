using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
#if CONTENT_UNDERSTANDING
using CommunityToolkit.DocumentExtraction.AzureContentUnderstanding;
using SdkClient = Azure.AI.ContentUnderstanding.ContentUnderstandingClient;
using SdkOptions = Azure.AI.ContentUnderstanding.ContentUnderstandingClientOptions;
using Adapter = CommunityToolkit.DocumentExtraction.AzureContentUnderstanding.ContentUnderstandingExtractionClient;
using AdapterOptions = CommunityToolkit.DocumentExtraction.AzureContentUnderstanding.ContentUnderstandingExtractionOptions;
using NativeResult = Azure.AI.ContentUnderstanding.AnalysisResult;
using NativeContent = Azure.AI.ContentUnderstanding.DocumentContent;
#else
using CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;
using SdkClient = Azure.AI.DocumentIntelligence.DocumentIntelligenceClient;
using SdkOptions = Azure.AI.DocumentIntelligence.DocumentIntelligenceClientOptions;
using Adapter = CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence.DocumentIntelligenceExtractionClient;
using AdapterOptions = CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence.DocumentIntelligenceExtractionOptions;
using NativeResult = Azure.AI.DocumentIntelligence.AnalyzeResult;
#endif

namespace CommunityToolkit.DocumentExtraction.Tests;

#if CONTENT_UNDERSTANDING
public sealed partial class ContentUnderstandingExtractionClientTests
#else
public sealed partial class DocumentIntelligenceExtractionClientTests
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
        var adapterOptions = new AdapterOptions { AnalyzerId = selector ?? Adapter.DefaultAnalyzerId };
        return borrowed
            ? new Adapter(new SdkClient(endpoint, credential, options), adapterOptions)
            : new Adapter(endpoint, credential, adapterOptions, options);
#else
        var adapterOptions = new AdapterOptions { DefaultModel = selector ?? "prebuilt-layout", IncludeImages = images };
        return borrowed
            ? new Adapter(new SdkClient(endpoint, credential, options), adapterOptions)
            : new Adapter(endpoint, credential, adapterOptions, options);
#endif
    }

    private static OfflineTransport Replay(byte[] bytes, params TrackedResponse[] tail)
        => new([TrackedResponse.Accepted(), new TrackedResponse(bytes), .. tail]);

    private static Task<ExtractionDocument> Extract(Adapter client, Stream input, DocumentExtractionOptions? options = null,
        CancellationToken token = default)
        => client.ExtractAsync(input, "invoice.pdf", "application/pdf", options, token);

    [Theory]
    [InlineData("omitted")]
    [InlineData("null")]
    [InlineData("configured")]
    public async Task AsDocumentExtractionClient_CapturesOptionsAndBorrowsConfiguredNativeClient(string mode)
    {
        var wire = AzureWireFixture.Bytes(AzureWireFixture.Create());
        var transport = new OfflineTransport(
            TrackedResponse.Accepted(), new TrackedResponse(wire),
            TrackedResponse.Accepted(), new TrackedResponse(wire),
            TrackedResponse.Accepted(), new TrackedResponse(wire));
        var sdkOptions = new SdkOptions { Transport = transport };
        sdkOptions.Retry.MaxRetries = 0;
        var credential = new FixedCredential();
        var sdk = new SdkClient(new Uri("https://offline.test"), credential, sdkOptions);
#if CONTENT_UNDERSTANDING
        var settings = new AdapterOptions { AnalyzerId = "captured-selector" };
#else
        var settings = new AdapterOptions { DefaultModel = "captured-selector" };
#endif
        using DocumentExtractionClient client = mode == "omitted" ? sdk.AsDocumentExtractionClient()
            : sdk.AsDocumentExtractionClient(mode == "null" ? null : settings);
        Assert.IsType<Adapter>(client);
        Assert.Same(sdk, client.GetService(typeof(SdkClient)));
#if CONTENT_UNDERSTANDING
        settings.AnalyzerId = "mutated-selector";
#else
        settings.DefaultModel = "mutated-selector";
        settings.IncludeImages = true;
#endif
        using var input = new MemoryStream([1, 2, 3]);
        var result = await client.ExtractAsync(input, "invoice.pdf", "application/pdf");
        Assert.IsType<NativeResult>(result.RawRepresentation);
        Assert.Contains(mode == "configured" ? "captured-selector"
            : AzureWireFixture.IsContentUnderstanding ? "prebuilt-document" : "prebuilt-layout",
            transport.Requests[0].Uri.AbsolutePath);
        Assert.DoesNotContain("output=figures", transport.Requests[0].Uri.Query);
        input.Position = 0;
        await client.ExtractAsync(input, "override.pdf", "application/pdf", new() { ExtractorName = "request-selector" });
        Assert.Contains("request-selector", transport.Requests[2].Uri.AbsolutePath);
        client.Dispose();
        using DocumentExtractionClient second = sdk.AsDocumentExtractionClient();
        input.Position = 0;
        await second.ExtractAsync(input, "still-borrowed.pdf", "application/pdf");
        Assert.Equal(6, transport.Requests.Count);
        Assert.True(credential.Calls > 0);
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void AsDocumentExtractionClient_InvalidSelectorAndNullReceiver_UseConstructorValidation(string? selector)
    {
        var sdk = new SdkClient(new Uri("https://offline.test"), new FixedCredential());
#if CONTENT_UNDERSTANDING
        var settings = new AdapterOptions { AnalyzerId = selector! };
#else
        var settings = new AdapterOptions { DefaultModel = selector! };
#endif
        Assert.ThrowsAny<ArgumentException>(() => sdk.AsDocumentExtractionClient(settings));
        SdkClient missing = null!;
        Assert.Throws<ArgumentNullException>(() => missing.AsDocumentExtractionClient());
    }

#if !CONTENT_UNDERSTANDING
    [Fact]
    public async Task AsDocumentExtractionClient_ConfiguredFigureSetting_IsCaptured()
    {
        byte[] imageBytes = [137, 80, 78, 71];
        var transport = Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()), new TrackedResponse(imageBytes));
        var sdkOptions = new SdkOptions { Transport = transport };
        sdkOptions.Retry.MaxRetries = 0;
        var sdk = new SdkClient(new Uri("https://offline.test"), new FixedCredential(), sdkOptions);
        var settings = new AdapterOptions { IncludeImages = true };
        using var client = sdk.AsDocumentExtractionClient(settings);
        settings.IncludeImages = false;
        using var input = new MemoryStream([1, 2, 3]);
        var result = await client.ExtractAsync(input, "figures.pdf", "application/pdf");
        Assert.Contains("output=figures", transport.Requests[0].Uri.Query);
        Assert.Equal(imageBytes, Assert.IsType<DataContent>(
            Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentImageElement>()).Content).Data.ToArray());
    }
#endif

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Constructor_NullOrDefaultOptions_PreservesDefaultsAndBorrowedSdk(bool borrowed, bool nullOptions)
    {
        var transport = Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()));
        var sdkOptions = new SdkOptions { Transport = transport };
        sdkOptions.Retry.MaxRetries = 0;
        var endpoint = new Uri("https://offline.test");
        var credential = new FixedCredential();
        var settings = nullOptions ? null : new AdapterOptions();
#if CONTENT_UNDERSTANDING
        Assert.Equal("prebuilt-document", new AdapterOptions().AnalyzerId);
#else
        Assert.Equal("prebuilt-layout", new AdapterOptions().DefaultModel);
        Assert.False(new AdapterOptions().IncludeImages);
#endif
        var sdk = new SdkClient(endpoint, credential, sdkOptions);
        using var client = borrowed ? new Adapter(sdk, settings) : new Adapter(endpoint, credential, settings, sdkOptions);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        Assert.Contains(AzureWireFixture.IsContentUnderstanding ? "prebuilt-document" : "prebuilt-layout",
            transport.Requests[0].Uri.AbsolutePath);
        Assert.DoesNotContain("output=figures", transport.Requests[0].Uri.Query);
        Assert.Null(Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentImageElement>()).Content);
        if (borrowed)
        {
            Assert.Same(sdk, client.GetService(typeof(SdkClient)));
        }

        client.Dispose();
        Assert.IsType<NativeResult>(result.RawRepresentation);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Constructor_OptionsMutation_DoesNotChangeCapturedSelectorOrFigureSetting(bool borrowed)
    {
        var transport = new OfflineTransport(
            TrackedResponse.Accepted(), new TrackedResponse(AzureWireFixture.Bytes(AzureWireFixture.Create())),
            TrackedResponse.Accepted(), new TrackedResponse(AzureWireFixture.Bytes(AzureWireFixture.Create())));
        var sdkOptions = new SdkOptions { Transport = transport };
        sdkOptions.Retry.MaxRetries = 0;
        var endpoint = new Uri("https://offline.test");
        var credential = new FixedCredential();
#if CONTENT_UNDERSTANDING
        var settings = new AdapterOptions { AnalyzerId = "captured-selector" };
#else
        var settings = new AdapterOptions { DefaultModel = "captured-selector", IncludeImages = false };
#endif
        using var client = borrowed
            ? new Adapter(new SdkClient(endpoint, credential, sdkOptions), settings)
            : new Adapter(endpoint, credential, settings, sdkOptions);
#if CONTENT_UNDERSTANDING
        settings.AnalyzerId = "mutated-selector";
#else
        settings.DefaultModel = "mutated-selector";
        settings.IncludeImages = true;
#endif
        using var input = new MemoryStream([1, 2, 3]);
        var first = await Extract(client, input);
        input.Position = 0;
        var requestOptions = new DocumentExtractionOptions { ExtractorName = "request-selector" };
        var second = await Extract(client, input, requestOptions);
        Assert.Contains("captured-selector", transport.Requests[0].Uri.AbsolutePath);
        Assert.Contains("request-selector", transport.Requests[2].Uri.AbsolutePath);
        Assert.DoesNotContain("mutated-selector", transport.Requests[0].Uri.AbsolutePath);
        Assert.All(transport.Requests, request => Assert.DoesNotContain("output=figures", request.Uri.Query));
        Assert.Null(Assert.Single(first.EnumerateContent().OfType<ExtractionDocumentImageElement>()).Content);
        Assert.Null(Assert.Single(second.EnumerateContent().OfType<ExtractionDocumentImageElement>()).Content);
        Assert.Equal("request-selector", requestOptions.ExtractorName);
        Assert.Equal(4, transport.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Constructor_InvalidOptionSelector_FailsForBothConstructionPaths(string? selector)
    {
        var sdk = new SdkClient(new Uri("https://offline.test"), new FixedCredential());
#if CONTENT_UNDERSTANDING
        var settings = new AdapterOptions { AnalyzerId = selector! };
#else
        var settings = new AdapterOptions { DefaultModel = selector! };
#endif
        Assert.ThrowsAny<ArgumentException>(() => new Adapter(sdk, settings));
        Assert.ThrowsAny<ArgumentException>(() =>
            new Adapter(new Uri("https://offline.test"), new FixedCredential(), settings));
    }

    [Fact]
    public void Constructors_ExposeOnlyToolkitOptionsAndDistinctNativeOptions()
    {
        var constructors = typeof(Adapter).GetConstructors();
        Assert.Equal(2, constructors.Length);
        Assert.Contains(constructors, constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual([typeof(SdkClient), typeof(AdapterOptions)]));
        Assert.Contains(constructors, constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual([typeof(Uri), typeof(TokenCredential), typeof(AdapterOptions), typeof(SdkOptions)]));
    }

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
        Assert.IsType<NativeResult>(result.RawRepresentation);
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
    }

    [Fact]
    public async Task ExtractAsync_SelectorAndProviderOptions_ReachReplayRequest()
    {
        var transport = Replay(AzureWireFixture.Bytes(AzureWireFixture.Create()));
        using var client = Create(transport, selector: "configured");
        using var input = new MemoryStream([1, 2, 3]);
        var options = new DocumentExtractionOptions { ExtractorName = "requested" };
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
        Assert.IsType<NativeResult>(result.RawRepresentation);
        Assert.Equal("DataZone", options.AdditionalProperties["processingLocation"]);
#else
        Assert.Contains("locale=fr-FR", request);
        Assert.Contains("pages=3", request);
        Assert.Contains("languages", request);
        Assert.Contains("barcodes", request);
        Assert.Equal("native-model", Assert.IsType<NativeResult>(result.RawRepresentation).ModelId);
        Assert.Equal("fr-FR", options.AdditionalProperties["locale"]);
#endif
        Assert.Equal("requested", options.ExtractorName);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task ExtractAsync_AcceptedRunningSucceeded_PreservesNativeIdentityAfterCleanup()
    {
        byte[] bytes = AzureWireFixture.Bytes(AzureWireFixture.Create());
        var running = new TrackedResponse(Encoding.UTF8.GetBytes("""{"status":"running","id":"receipt"}"""));
        var final = new TrackedResponse(bytes);
        var transport = new OfflineTransport(TrackedResponse.Accepted(), running, final);
        var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        client.Dispose();
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var content = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
        object page = content.Pages[0];
        object paragraph = content.Paragraphs[0];
#else
        object page = native.Pages[0];
        object paragraph = native.Paragraphs[0];
        Assert.Equal("native-model", native.ModelId);
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
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var content = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
        Assert.Same(content.Tables[0], table.Sources[0].GetRawRepresentation());
        Assert.Same(content.Tables[0].Cells[0], cell.Sources[0].GetRawRepresentation());
        Assert.Same(content.Figures[0], figure.Sources[0].GetRawRepresentation());
#else
        Assert.Same(native.Tables[0], table.Sources[0].GetRawRepresentation());
        Assert.Same(native.Tables[0].Cells[0], cell.Sources[0].GetRawRepresentation());
        Assert.Same(native.Figures[0], figure.Sources[0].GetRawRepresentation());
#endif
        Assert.Null(figure.Text); // A caption is not OCR/transcription.
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
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
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
            element => Assert.Equal("Invoice heading", Assert.IsType<TextContent>(Assert.IsType<ExtractionDocumentTextElement>(element).Content).Text),
            element => Assert.Equal("Revenue chart", Assert.IsType<ExtractionDocumentImageElement>(element).AlternativeText));
        Assert.Equal(new[] { typeof(ExtractionDocumentTableElement), typeof(ExtractionDocumentTextElement), typeof(ExtractionDocumentImageElement) },
            result.EnumerateContent().Select(element => element.GetType()));
        Assert.Same(table, result.EnumerateContent().First());
        Assert.Same(result.SourcePages[0], Assert.Single(inner.Sources).Page);
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
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
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var page = Assert.IsType<NativeContent>(Assert.Single(native.Contents)).Pages[0];
#else
        var page = native.Pages[0];
#endif
        Assert.Same(page.Lines[0], lines[0].Sources[0].GetRawRepresentation());
        Assert.Equal(2, result.Sections[0].Elements.Count);
    }

    [Fact]
    public async Task ExtractAsync_MultiPageParagraph_PreservesNativeIdentityAndOrderedOriginalFrames()
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        var secondPage = content["pages"]![0]!.DeepClone();
        secondPage["pageNumber"] = 8;
        content["pages"]!.AsArray().Add(secondPage);
        var paragraph = content["paragraphs"]![0]!;
#if CONTENT_UNDERSTANDING
        paragraph["source"] = "D(3,1,2,3,2,3,4,2,5,1,4);D(8)";
#else
        paragraph["boundingRegions"]!.AsArray().Add(new JsonObject
        {
            ["pageNumber"] = 8,
            ["polygon"] = new JsonArray(),
        });
#endif
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        Assert.Equal(new int?[] { 3, 8 }, result.SourcePages.Select(page => page.PageNumber));
        var text = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTextElement>());
        Assert.Equal("Invoice heading", Assert.IsType<TextContent>(text.Content).Text);
        Assert.Equal("heading", text.Role);
        Assert.Equal(2, text.Sources.Count);
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativeParagraph = Assert.Single(Assert.IsType<NativeContent>(Assert.Single(native.Contents)).Paragraphs);
#else
        var nativeParagraph = Assert.Single(native.Paragraphs);
#endif
        for (int i = 0; i < 2; i++)
        {
            Assert.Same(result.SourcePages[i], text.Sources[i].Page);
            Assert.Same(nativeParagraph, text.Sources[i].GetRawRepresentation());
        }
        Assert.Equal(AzureWireFixture.Points, text.Sources[0].Polygon.Select(point => (point.X, point.Y)));
        Assert.Empty(text.Sources[1].Polygon);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Fact]
    public async Task ExtractAsync_BlankNativePage_PreservesFrameAndNativeAssociationWithoutInventedContent()
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["paragraphs"] = new JsonArray();
        content["tables"] = new JsonArray();
        content["figures"] = new JsonArray();
#if CONTENT_UNDERSTANDING
        content["markdown"] = "";
#endif
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var page = Assert.Single(result.SourcePages);
        Assert.Equal(3, page.PageNumber);
        Assert.Equal(new ExtractionDocumentPageDimensions(300, 400), page.Dimensions);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Pixel, page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, page.CoordinateOrigin);
        var section = Assert.Single(result.Sections);
        Assert.Empty(section.Elements);
        Assert.Empty(result.EnumerateContent());
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativePage = Assert.Single(Assert.IsType<NativeContent>(Assert.Single(native.Contents)).Pages);
#else
        var nativePage = Assert.Single(native.Pages);
#endif
        var source = Assert.Single(section.Sources);
        Assert.Same(page, source.Page);
        Assert.Same(nativePage, source.GetRawRepresentation());
        Assert.Empty(source.Polygon);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
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
        Assert.Equal(typeof(DocumentExtractionClient), typeof(Adapter).BaseType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(Adapter).GetMethod(nameof(client.ExtractAsync))!.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(Adapter).GetMethod(nameof(client.GetService))!.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(Adapter).GetMethod(nameof(client.Dispose))!.DeclaringType);
        Assert.Same(client, client.GetService(typeof(Adapter)));
        Assert.Same(client, client.GetService(typeof(DocumentExtractionClient)));
        Assert.Same(client, client.GetService(typeof(object)));
        Assert.IsType<SdkClient>(client.GetService(typeof(SdkClient)));
        Assert.Null(client.GetService(typeof(Stream)));
        Assert.Null(client.GetService(typeof(SdkClient), "key"));
        Assert.Null(client.GetService(typeof(DocumentExtractionClient), "key"));
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
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
        Assert.Null(figure.Text);
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
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
#if CONTENT_UNDERSTANDING
            content["markdown"] = "";
#endif
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
        var result = await Extract(client, input);
        Assert.Empty(result.SourcePages);
        Assert.IsType<NativeResult>(result.RawRepresentation);
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

    // SYNTHETIC PINNED-SDK REPLAYS: these native cell.Elements combinations test
    // offline mapping, not live OCR recognition or service emission guarantees.
    // Geometry and spans deliberately overlap in AzureWireFixture; only explicit
    // native pointers establish cell ownership. Markdown is never a substitute.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_MixedCell_NativePointersMapExactChildrenInSuppliedOrder(bool reverse)
    {
        string[] pointers = reverse
            ? ["/figures/0", "/paragraphs/0", "/tables/1"]
            : ["/tables/1", "/paragraphs/0", "/figures/0"];
        var root = MixedCellReplay(pointers);
        var content = AzureWireFixture.Content(root);
        var nested = content["tables"]![0]!.DeepClone();
        nested["cells"]![0]!.AsObject().Remove("elements");
        nested["cells"]![0]!["content"] = "Nested cell text";
        content["tables"]!.AsArray().Add(nested);
        var transport = Replay(AzureWireFixture.Bytes(root));
        var client = Create(transport, borrowed: true);
        var sdk = client.GetService(typeof(SdkClient));
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        client.Dispose();
        Assert.Same(sdk, client.GetService(typeof(SdkClient)));
        var page = Assert.Single(result.Sections);
        var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(page.Elements));
        var container = AssertMixedCellContainer(result, table);
        Assert.Equal(3, container.Elements.Count);
        var nestedTable = Assert.IsType<ExtractionDocumentTableElement>(container.Elements[reverse ? 2 : 0]);
        var paragraph = Assert.IsType<ExtractionDocumentTextElement>(container.Elements[1]);
        var figure = Assert.IsType<ExtractionDocumentImageElement>(container.Elements[reverse ? 0 : 2]);
        Assert.Equal("Invoice heading", Assert.IsType<TextContent>(paragraph.Content).Text);
        Assert.Equal("heading", paragraph.Role);
        Assert.Equal("Nested cell text", Assert.IsType<TextContent>(
            Assert.IsType<ExtractionDocumentTextElement>(nestedTable.Cells[0, 0]!.Content).Content).Text);
        Assert.Equal("Revenue chart", figure.AlternativeText);
        Assert.Null(figure.Content);
        Assert.Null(figure.Text);
        Assert.Equal(AzureWireFixture.Points, figure.Sources[0].Polygon.Select(point => (point.X, point.Y)));
        Assert.Equal(AzureWireFixture.Points, paragraph.Sources[0].Polygon.Select(point => (point.X, point.Y)));
        Assert.All(paragraph.Sources, source => Assert.Same(result.SourcePages[0], source.Page));
        Assert.Same(result.SourcePages[0], Assert.Single(figure.Sources).Page);
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativeContent = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
#else
        var nativeContent = native;
#endif
        Assert.Equal(pointers, nativeContent.Tables[0].Cells[0].Elements);
        Assert.Equal("Total amount", nativeContent.Tables[0].Cells[0].Content);
        Assert.Same(nativeContent.Tables[0], table.Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Tables[0].Cells[0], table.Cells[0, 0]!.Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Tables[1], nestedTable.Sources[0].GetRawRepresentation());
        Assert.All(paragraph.Sources, source => Assert.Same(nativeContent.Paragraphs[0], source.GetRawRepresentation()));
        Assert.Same(nativeContent.Figures[0], figure.Sources[0].GetRawRepresentation());
        // All three mapped children have one structured owner, and no aggregate
        // native fallback is also synthesized as a local child.
        Assert.DoesNotContain(container.Elements.OfType<ExtractionDocumentTextElement>(),
            text => text.Content is TextContent payload && payload.Text == "Total amount");
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }

    [Fact]
    public async Task ExtractAsync_MixedCell_ActualFigureRefIndependentlyMapsImageAndNativeIdentity()
    {
        var root = MixedCellReplay("/figures/0");
#if CONTENT_UNDERSTANDING
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
#else
        byte[] imageBytes = [137, 80, 78, 71, 13, 10, 26, 10, 42];
        var imageResponse = new TrackedResponse(imageBytes);
        var transport = Replay(AzureWireFixture.Bytes(root), imageResponse);
        using var client = Create(transport, images: true);
#endif
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var page = Assert.Single(result.Sections);
        var table = Assert.Single(page.Elements.OfType<ExtractionDocumentTableElement>());
        var container = AssertMixedCellContainer(result, table);
        var figure = Assert.IsType<ExtractionDocumentImageElement>(Assert.Single(container.Elements));
        Assert.Equal("Revenue chart", figure.AlternativeText);
        Assert.Null(figure.Text);
        Assert.Same(result.SourcePages[0], Assert.Single(figure.Sources).Page);
        Assert.Equal(AzureWireFixture.Points, figure.Sources[0].Polygon.Select(point => (point.X, point.Y)));
        Assert.Equal(2, page.Elements.Count); // Unreferenced paragraph remains a root sibling.
        Assert.Single(page.Elements.OfType<ExtractionDocumentTextElement>());
        Assert.DoesNotContain(figure, page.Elements);
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativeContent = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
        Assert.Null(figure.Content); // Description/source do not fabricate bytes or a URI.
        Assert.Equal(2, transport.Requests.Count);
#else
        var nativeContent = native;
        var image = Assert.IsType<DataContent>(figure.Content);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(imageBytes, image.Data.ToArray());
        Assert.Contains("output=figures", transport.Requests[0].Uri.Query);
        Assert.Equal(RequestMethod.Get, transport.Requests[2].Method);
        Assert.EndsWith("/figures/3.1", transport.Requests[2].Uri.AbsolutePath);
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal(2, imageResponse.DisposeCount);
#endif
        Assert.Equal(new[] { "/figures/0" }, nativeContent.Tables[0].Cells[0].Elements);
        Assert.Same(nativeContent.Figures[0], figure.Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Tables[0].Cells[0], container.Sources[0].GetRawRepresentation());
        Assert.Equal("Total amount", nativeContent.Tables[0].Cells[0].Content);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, "Total amount")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "Total amount")]
    public async Task ExtractAsync_MixedCell_AbsentOrEmptyRefsKeepNativeFallbackWithoutInference(bool emptyRefs, string? fallback)
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        var wireCell = content["tables"]![0]!["cells"]![0]!;
        if (emptyRefs)
        {
            wireCell["elements"] = new JsonArray();
        }
        if (fallback is null)
        {
            wireCell.AsObject().Remove("content");
        }
        else
        {
            wireCell["content"] = fallback;
        }
        // Deliberately misleading Markdown and overlapping fixture geometry must
        // not create child links absent native cell.Elements.
        content[AzureWireFixture.IsContentUnderstanding ? "markdown" : "content"] =
            "| Invoice heading | ![Revenue chart](/figures/0) |";
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var page = Assert.Single(result.Sections);
        Assert.Equal(3, page.Elements.Count);
        Assert.Single(page.Elements.OfType<ExtractionDocumentTextElement>());
        Assert.Single(page.Elements.OfType<ExtractionDocumentImageElement>());
        var table = Assert.Single(page.Elements.OfType<ExtractionDocumentTableElement>());
        var cell = table.Cells[0, 0]!;
        Assert.Equal((0, 0, 2, 2, "columnHeader"),
            (cell.RowIndex, cell.ColumnIndex, cell.RowSpan, cell.ColumnSpan, cell.Role));
        var source = Assert.Single(cell.Sources);
        Assert.Equal(AzureWireFixture.Points, source.Polygon.Select(point => (point.X, point.Y)));
        Assert.Same(result.SourcePages[0], source.Page);
        if (fallback is null)
        {
            Assert.Null(cell.Content);
        }
        else
        {
            var text = Assert.IsType<ExtractionDocumentTextElement>(cell.Content);
            Assert.Equal(fallback, Assert.IsType<TextContent>(text.Content).Text);
            Assert.Same(source, Assert.Single(text.Sources));
        }
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativeCell = Assert.IsType<NativeContent>(Assert.Single(native.Contents)).Tables[0].Cells[0];
#else
        var nativeCell = native.Tables[0].Cells[0];
#endif
        Assert.Empty(nativeCell.Elements);
        Assert.Equal(fallback, nativeCell.Content);
        Assert.Same(nativeCell, source.GetRawRepresentation());
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_MixedCell_AncestorSectionRedundancyYieldsToCellOwnership(bool nested)
    {
        var root = MixedCellReplay("/figures/0", "/paragraphs/0");
        AzureWireFixture.Content(root)["sections"] = nested
            ? new JsonArray(
                AzureWireFixture.Section("/paragraphs/0", "/sections/1", "/figures/0"),
                AzureWireFixture.Section("/figures/0", "/tables/0", "/paragraphs/0"))
            : new JsonArray(AzureWireFixture.Section("/paragraphs/0", "/tables/0", "/figures/0"));
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var outer = Assert.IsType<ExtractionDocumentSection>(Assert.Single(Assert.Single(result.Sections).Elements));
        var owner = nested ? Assert.IsType<ExtractionDocumentSection>(Assert.Single(outer.Elements)) : outer;
        var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(owner.Elements));
        var container = AssertMixedCellContainer(result, table);
        Assert.Collection(container.Elements,
            element => Assert.Equal("Revenue chart", Assert.IsType<ExtractionDocumentImageElement>(element).AlternativeText),
            element => Assert.Equal("Invoice heading", Assert.IsType<TextContent>(Assert.IsType<ExtractionDocumentTextElement>(element).Content).Text));
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativeContent = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
#else
        var nativeContent = native;
#endif
        Assert.Same(nativeContent.Sections[nested ? 1 : 0], owner.Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Figures[0], container.Elements[0].Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Paragraphs[0], container.Elements[1].Sources[0].GetRawRepresentation());
        // Native sibling lists remain native; only the mapped redundant placement
        // disappears. This is not permission to mutate the raw SDK result.
        Assert.Equal(new[] { "/figures/0", "/paragraphs/0" }, nativeContent.Tables[0].Cells[0].Elements);
        Assert.Contains("/paragraphs/0", nativeContent.Sections[nested ? 1 : 0].Elements);
        Assert.Contains("/figures/0", nativeContent.Sections[nested ? 1 : 0].Elements);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }

    [Fact]
    public async Task ExtractAsync_MixedCell_ValidSectionRefRetainsOrderedSectionChildren()
    {
        var root = MixedCellReplay("/sections/0");
        AzureWireFixture.Content(root)["sections"] =
            new JsonArray(AzureWireFixture.Section("/figures/0", "/paragraphs/0"));
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(Assert.Single(result.Sections).Elements));
        var container = AssertMixedCellContainer(result, table);
        var section = Assert.IsType<ExtractionDocumentSection>(Assert.Single(container.Elements));
        Assert.Collection(section.Elements,
            element => Assert.Equal("Revenue chart", Assert.IsType<ExtractionDocumentImageElement>(element).AlternativeText),
            element => Assert.Equal("Invoice heading", Assert.IsType<TextContent>(Assert.IsType<ExtractionDocumentTextElement>(element).Content).Text));
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativeContent = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
#else
        var nativeContent = native;
#endif
        Assert.Equal(new[] { "/sections/0" }, nativeContent.Tables[0].Cells[0].Elements);
        Assert.Same(nativeContent.Sections[0], section.Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Figures[0], section.Elements[0].Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Paragraphs[0], section.Elements[1].Sources[0].GetRawRepresentation());
        Assert.Same(result.SourcePages[0], section.Sources[0].Page);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_MixedCell_AncestorRedundancyOfSectionDescendantsKeepsOnlyDeepOwner(bool innerFirst)
    {
        int innerIndex = innerFirst ? 0 : 1;
        var root = MixedCellReplay($"/sections/{innerIndex}");
        var inner = AzureWireFixture.Section("/figures/0", "/paragraphs/0");
        var outer = AzureWireFixture.Section("/paragraphs/0", "/tables/0", $"/sections/{innerIndex}", "/figures/0");
        AzureWireFixture.Content(root)["sections"] = innerFirst
            ? new JsonArray(inner, outer) : new JsonArray(outer, inner);
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var mappedOuter = Assert.IsType<ExtractionDocumentSection>(Assert.Single(Assert.Single(result.Sections).Elements));
        var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(mappedOuter.Elements));
        var container = AssertMixedCellContainer(result, table);
        var mappedInner = Assert.IsType<ExtractionDocumentSection>(Assert.Single(container.Elements));
        Assert.Collection(mappedInner.Elements,
            element => Assert.Equal("Revenue chart", Assert.IsType<ExtractionDocumentImageElement>(element).AlternativeText),
            element => Assert.Equal("Invoice heading", Assert.IsType<TextContent>(element.Content).Text));
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#if CONTENT_UNDERSTANDING
        var nativeContent = Assert.IsType<NativeContent>(Assert.Single(native.Contents));
#else
        var nativeContent = native;
#endif
        Assert.Same(nativeContent.Sections[innerIndex], mappedInner.Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Figures[0], mappedInner.Elements[0].Sources[0].GetRawRepresentation());
        Assert.Same(nativeContent.Paragraphs[0], mappedInner.Elements[1].Sources[0].GetRawRepresentation());
        Assert.Contains("/paragraphs/0", nativeContent.Sections[1 - innerIndex].Elements);
        Assert.Contains("/figures/0", nativeContent.Sections[1 - innerIndex].Elements);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unsupported")]
    [InlineData("dangling-paragraph")]
    [InlineData("dangling-table")]
    [InlineData("dangling-figure")]
    [InlineData("dangling-section")]
    [InlineData("duplicate-ref")]
    [InlineData("duplicate-section-ref")]
    [InlineData("cross-cell-same-table")]
    [InlineData("cross-cell-across-tables")]
    [InlineData("cross-cell-table")]
    [InlineData("cross-cell-figure")]
    [InlineData("cross-cell-section")]
    [InlineData("parent-table-self")]
    [InlineData("cross-table-cycle")]
    [InlineData("section-mediated-cycle")]
    [InlineData("cell-section-table-cycle")]
    [InlineData("section-cycle")]
    [InlineData("unrelated-section")]
    [InlineData("ambiguous-sections")]
    [InlineData("indirect-unrelated-section")]
    [InlineData("indirect-cross-cell")]
    public async Task ExtractAsync_MixedCell_InvalidOwnershipGraphRejectsAndCleansUp(string scenario)
    {
        var transport = Replay(AzureWireFixture.Bytes(InvalidMixedCellReplay(scenario)));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Extract(client, input));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }

    [Theory]
    [InlineData("pre-copy")]
    [InlineData("polling")]
    [InlineData("mapping")]
    public async Task ExtractAsync_MixedCell_CancellationKeepsBorrowedInputAndFinalCleanup(string stage)
    {
        var final = new TrackedResponse(AzureWireFixture.Bytes(MixedCellReplay("/paragraphs/0", "/figures/0")));
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
        using var client = Create(transport, borrowed: true);
        using var input = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Extract(client, input, token: cancellation.Token));
        Assert.Equal(stage == "pre-copy" ? 0 : stage == "polling" ? 1 : 2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.True(request.Token.CanBeCanceled));
        if (stage == "mapping")
        {
            Assert.True(final.DisposeCount > 0);
        }
        AssertMixedCellBorrowedInput(input);
    }

#if !CONTENT_UNDERSTANDING
    [Theory]
    [InlineData("http")]
    [InlineData("read")]
    [InlineData("cancel")]
    public async Task ExtractAsync_MixedCell_FigureFailureKeepsPrimaryOutcomeAndCleansUp(string scenario)
    {
        var figure = new TrackedResponse(scenario == "http"
            ? Encoding.UTF8.GetBytes("""{"error":{"code":"FigureFailure","message":"figure unavailable"}}""")
            : [1, 2, 3], scenario == "http" ? 400 : 200);
        if (scenario == "read")
        {
            figure.ContentStream = new FaultingBody([1, 2, 3], new IOException("mixed-cell figure read failure"), seekable: false);
        }
        var transport = Replay(AzureWireFixture.Bytes(MixedCellReplay("/figures/0")), figure);
        using var cancellation = new CancellationTokenSource();
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
        var error = await Record.ExceptionAsync(() => Extract(client, input, token: cancellation.Token));
        if (scenario == "http")
        {
            Assert.Contains("FigureFailure", Assert.IsType<RequestFailedException>(error).Message);
        }
        else if (scenario == "read")
        {
            Assert.Equal("mixed-cell figure read failure", Assert.IsType<IOException>(error).Message);
        }
        else
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        }
        Assert.Equal(3, transport.Requests.Count);
        Assert.EndsWith("/figures/3.1", transport.Requests[2].Uri.AbsolutePath);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        Assert.True(figure.DisposeCount > 0);
        AssertMixedCellBorrowedInput(input);
    }

    [Fact]
    public async Task ExtractAsync_MixedCell_InvalidOwnershipAfterFigureDownloadCleansBothResponses()
    {
        var figure = new TrackedResponse([137, 80, 78, 71, 13, 10, 26, 10, 42]);
        var transport = Replay(AzureWireFixture.Bytes(MixedCellReplay("/figures/0", "/figures/0")), figure);
        using var client = Create(transport, images: true);
        using var input = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Extract(client, input));
        Assert.Equal(3, transport.Requests.Count);
        Assert.EndsWith("/figures/3.1", transport.Requests[2].Uri.AbsolutePath);
        Assert.Equal(2, transport.Delivered[1].DisposeCount);
        Assert.Equal(2, figure.DisposeCount);
        AssertMixedCellBorrowedInput(input);
    }
#endif

    private static JsonObject MixedCellReplay(params string[] pointers)
    {
        // Synthetic fixture, schema-shaped using AzureWireFixture's pinned native
        // models. A string array deserializes to native DocumentTableCell.Elements.
        var root = AzureWireFixture.Create();
        AzureWireFixture.Content(root)["tables"]![0]!["cells"]![0]!["elements"] =
            JsonSerializer.SerializeToNode(pointers);
        return root;
    }

    private static JsonObject InvalidMixedCellReplay(string scenario)
    {
        var root = MixedCellReplay("/paragraphs/0");
        var content = AzureWireFixture.Content(root);
        var tables = content["tables"]!.AsArray();
        var firstCell = tables[0]!["cells"]![0]!;
        void SetRefs(JsonNode cell, params string[] refs) => cell["elements"] = JsonSerializer.SerializeToNode(refs);
        void AddOtherTable(params string[] refs)
        {
            var other = tables[0]!.DeepClone();
            SetRefs(other["cells"]![0]!, refs);
            tables.Add(other);
        }
        switch (scenario)
        {
            case "malformed": SetRefs(firstCell, "paragraphs/0"); break;
            case "unsupported": SetRefs(firstCell, "/pages/0"); break;
            case "dangling-paragraph": SetRefs(firstCell, "/paragraphs/999"); break;
            case "dangling-table": SetRefs(firstCell, "/tables/999"); break;
            case "dangling-figure": SetRefs(firstCell, "/figures/999"); break;
            case "dangling-section": SetRefs(firstCell, "/sections/999"); break;
            case "duplicate-ref": SetRefs(firstCell, "/paragraphs/0", "/paragraphs/0"); break;
            case "duplicate-section-ref":
                SetRefs(firstCell, "/sections/0", "/sections/0");
                content["sections"] = new JsonArray(AzureWireFixture.Section("/paragraphs/0"));
                break;
            case "cross-cell-same-table":
                var otherCell = firstCell.DeepClone();
                otherCell["columnIndex"] = 2;
                otherCell["columnSpan"] = 1;
                tables[0]!["cells"]!.AsArray().Add(otherCell);
                break;
            case "cross-cell-across-tables": AddOtherTable("/paragraphs/0"); break;
            case "cross-cell-table":
                SetRefs(firstCell, "/tables/2");
                AddOtherTable("/tables/2");
                var leaf = tables[0]!.DeepClone();
                leaf["cells"]![0]!.AsObject().Remove("elements");
                tables.Add(leaf);
                break;
            case "cross-cell-figure":
                SetRefs(firstCell, "/figures/0");
                AddOtherTable("/figures/0");
                break;
            case "cross-cell-section":
                SetRefs(firstCell, "/sections/0");
                AddOtherTable("/sections/0");
                content["sections"] = new JsonArray(AzureWireFixture.Section("/paragraphs/0"));
                break;
            case "parent-table-self": SetRefs(firstCell, "/tables/0"); break;
            case "cross-table-cycle":
                SetRefs(firstCell, "/tables/1");
                AddOtherTable("/tables/0");
                break;
            case "section-mediated-cycle":
                SetRefs(firstCell, "/sections/0");
                AddOtherTable("/tables/0");
                content["sections"] = new JsonArray(AzureWireFixture.Section("/tables/1"));
                break;
            case "cell-section-table-cycle":
                SetRefs(firstCell, "/sections/0");
                content["sections"] = new JsonArray(AzureWireFixture.Section("/tables/0"));
                break;
            case "section-cycle":
                SetRefs(firstCell, "/sections/0");
                content["sections"] = new JsonArray(
                    AzureWireFixture.Section("/sections/1"), AzureWireFixture.Section("/sections/0"));
                break;
            case "unrelated-section":
                content["sections"] = new JsonArray(
                    AzureWireFixture.Section("/tables/0"), AzureWireFixture.Section("/paragraphs/0"));
                break;
            case "ambiguous-sections":
                content["sections"] = new JsonArray(
                    AzureWireFixture.Section("/tables/0", "/paragraphs/0"), AzureWireFixture.Section("/paragraphs/0"));
                break;
            case "indirect-unrelated-section":
                SetRefs(firstCell, "/sections/0");
                content["sections"] = new JsonArray(
                    AzureWireFixture.Section("/paragraphs/0"), AzureWireFixture.Section("/paragraphs/0"));
                break;
            case "indirect-cross-cell":
                SetRefs(firstCell, "/sections/0");
                AddOtherTable("/sections/1");
                content["sections"] = new JsonArray(
                    AzureWireFixture.Section("/paragraphs/0"), AzureWireFixture.Section("/paragraphs/0"));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
        return root;
    }

    private static ExtractionDocumentSection AssertMixedCellContainer(ExtractionDocument result, ExtractionDocumentTableElement table)
    {
        Assert.Equal((2, 3), (table.Cells.RowCount, table.Cells.ColumnCount));
        Assert.Null(table.Cells[0, 2]);
        var cell = table.Cells[0, 0]!;
        Assert.Equal((0, 0, 2, 2, "columnHeader"),
            (cell.RowIndex, cell.ColumnIndex, cell.RowSpan, cell.ColumnSpan, cell.Role));
        var source = Assert.Single(cell.Sources);
        Assert.Same(Assert.Single(result.SourcePages), source.Page);
        Assert.Equal(AzureWireFixture.Points, source.Polygon.Select(point => (point.X, point.Y)));
        var container = Assert.IsType<ExtractionDocumentSection>(cell.Content);
        Assert.Null(container.Content);
        Assert.Same(source, Assert.Single(container.Sources));
        return container;
    }

    private static void AssertMixedCellBorrowedInput(MemoryStream input)
    {
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
