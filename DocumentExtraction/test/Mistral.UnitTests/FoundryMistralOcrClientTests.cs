// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using System.Text.Json;

using CommunityToolkit.DocumentExtraction.Mistral.UnitTests.TestSupport;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.Mistral.UnitTests;

public sealed class FoundryMistralOcrClientTests
{
    // Deliberate property order, spaces, escaped Unicode, and a trailing newline:
    // expected capture is these bytes, never a serialization of mapped/native objects.
    private static readonly byte[] s_fidelityResponse = Encoding.UTF8.GetBytes(
        """
        {
          "pages" : [ { "index": 4, "dimensions": { "height":480, "width":640 },
          "markdown":"before\n[tbl-0](tbl-0)\nafter \u03a9",
          "tables":[{"id":"tbl-0","content":"<table><tr><th colspan=\"2\">Invoice</th></tr><tr><td></td><td>17.50</td></tr></table>","format":"html"}],
          "images":[{"id":"img-7","image_base64":"data:image/png;base64,AAH/gA==","top_left_x":1.5,"top_left_y":2,"bottom_right_x":30,"bottom_right_y":40}] } ],
          "model" : "genuine-wire-model", "usage_info" : {"pages_processed":1}
        }
        """ + "\n");

    [Fact]
    public async Task ExtractAsync_RequestBytesAndOptions_UseExactDataUriAndRequestedModel()
    {
        using var session = new OfflineSession();
        DocumentExtractionOptions options = RequestedOptions();
        ExtractionDocument document = await Extract(session, options);
        RecordedRequest recorded = Assert.Single(session.Handler.Requests);
        AssertRequest(recorded);
        Assert.Equal("genuine-request-model", options.ExtractorName);
        Assert.Equal(2, options.AdditionalProperties["imageLimit"]);
        Assert.Equal("invoice.pdf", document.Identifier);
        Assert.Empty(document.Sections);
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_CaptureOptionsMatrix_PreservesExactJsonBytesAndDetachedChildren(bool? capture)
    {
        using var session = new OfflineSession(s_fidelityResponse);
        var options = capture is null ? null : new DocumentExtractionOptions { CaptureRawContent = capture.Value };
        ExtractionDocument document = await Extract(session, options);
        ExtractionDocumentSourceOutput output = Assert.Single(document.SourceOutputs);
        ExtractionDocumentSourcePage page = Assert.Single(document.SourcePages);
        ExtractionDocumentSection section = Assert.Single(document.Sections);
        Assert.Equal(["before\n", "<table><tr><th colspan=\"2\">Invoice</th></tr><tr><td></td><td>17.50</td></tr></table>", "\nafter Ω"],
            section.Elements.Where(e => e.Content is TextContent).Select(Text));
        var table = Assert.IsType<ExtractionDocumentTableElement>(section.Elements[1]);
        var image = Assert.IsType<ExtractionDocumentImageElement>(section.Elements[3]);
        AssertLiteralTable(table);
        Assert.Same(output, page.SourceOutput);
        Assert.Same(page, Assert.Single(table.Sources).Page);
        Assert.Same(page, Assert.Single(image.Sources).Page);
        Assert.Equal(5, page.PageNumber);

        // Operation's JsonDocument is already disposed; also dispose client/HTTP before native inspection.
        await session.AssertCleanAndBorrowedAsync(1, 1);
        session.Http.Dispose();
        JsonElement root = Assert.IsType<JsonElement>(output.GetRawRepresentation());
        Assert.Equal("genuine-wire-model", root.GetProperty("model").GetString());
        Assert.Equal(1, root.GetProperty("usage_info").GetProperty("pages_processed").GetInt32());
        JsonElement nativePage = Assert.IsType<JsonElement>(Assert.Single(section.Sources).GetRawRepresentation());
        Assert.Equal("before\n[tbl-0](tbl-0)\nafter Ω", nativePage.GetProperty("markdown").GetString());
        JsonElement nativeTable = Assert.IsType<JsonElement>(Assert.Single(table.Sources).GetRawRepresentation());
        Assert.Equal("html", nativeTable.GetProperty("format").GetString());
        Assert.Equal(Text(table), nativeTable.GetProperty("content").GetString());
        JsonElement nativeImage = Assert.IsType<JsonElement>(Assert.Single(image.Sources).GetRawRepresentation());
        Assert.Equal("img-7", nativeImage.GetProperty("id").GetString());
        Assert.Equal([0, 1, 255, 128], Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Equal(capture == true ? ExtractionDocumentRawContentStatus.Retained : ExtractionDocumentRawContentStatus.Disabled,
            output.ContentStatus);
        if (capture == true)
        {
            ExtractionDocumentArtifact artifact = Assert.IsType<ExtractionDocumentArtifact>(output.GetRawContent());
            Assert.Equal("application/json", artifact.MediaType);
            Assert.Equal(s_fidelityResponse, artifact.Content.ToArray());
            Assert.Null(artifact.Source);
        }
        else
        {
            Assert.Null(output.GetRawContent());
        }
    }

    [Theory]
    [InlineData("""{"model":"actual-v9","pages":[]}""", "actual-v9")]
    [InlineData("""{"pages":[]}""", null)]
    public async Task ExtractAsync_ResponseModelDiffersFromExtractorName_UsesActualModelId(string json, string? actual)
    {
        using var session = new OfflineSession(Encoding.UTF8.GetBytes(json));
        ExtractionDocument document = await Extract(session, RequestedOptions());
        ExtractionDocumentSourceOutput output = Assert.Single(document.SourceOutputs);
        Assert.Equal(actual, output.ModelId);
        Assert.Equal("foundry-mistral-ocr", output.ProviderName);
        Assert.Equal("invoice.pdf:foundry-mistral-ocr:output", output.Identifier);
        AssertRequest(Assert.Single(session.Handler.Requests));
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("html", "<table><tr><td>A &amp; B</td><td>17.50</td></tr></table>")]
    [InlineData("markdown", "| A & B | 17.50 |\n| --- | --- |")]
    public async Task ExtractAsync_UniqueTableMarker_PreservesBeforeTableAfterOrder(string format, string literal)
    {
        using var session = new OfflineSession(PageResponse(
            "before\n[tbl-0](tbl-0)\nbetween\n[tbl-1](tbl-1)\nafter",
            [new { id = "tbl-1", content = literal, format }, new { id = "tbl-0", content = literal, format }]));
        ExtractionDocument document = await Extract(session);
        ExtractionDocumentSection section = Assert.Single(document.Sections);
        Assert.Equal(["before\n", literal, "\nbetween\n", literal, "\nafter"], section.Elements.Select(Text));
        ExtractionDocumentTableElement[] tables = section.Elements.OfType<ExtractionDocumentTableElement>().ToArray();
        Assert.Equal(["tbl-0", "tbl-1"], tables.Select(t =>
            Assert.IsType<JsonElement>(Assert.Single(t.Sources).GetRawRepresentation()).GetProperty("id").GetString()));
        Assert.NotSame(tables[0], tables[1]);
        Assert.All(tables, AssertLiteralTable);
        Assert.All(section.Elements.SelectMany(e => e.Sources), s => Assert.Same(document.SourcePages[0], s.Page));
        Assert.Equal(section.Elements, document.EnumerateContent());
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("""{"id":"t","content":"","markdown":"legacy"}""", "")]
    [InlineData("""{"id":"t","content":" ","markdown":"legacy"}""", " ")]
    [InlineData("""{"id":"t","content":"current","markdown":"legacy"}""", "current")]
    [InlineData("""{"id":"t","markdown":"legacy"}""", "legacy")]
    [InlineData("""{"id":"t","content":null,"markdown":"legacy"}""", "legacy")]
    [InlineData("""{"id":"t","content":42,"markdown":"legacy"}""", "legacy")]
    public async Task ExtractAsync_TableContentIncludingEmpty_WinsOverLegacyMarkdown(string tableJson, string expected)
    {
        using JsonDocument fixture = JsonDocument.Parse(tableJson);
        using var session = new OfflineSession(PageResponse("[t](t)", [fixture.RootElement]));
        ExtractionDocument document = await Extract(session);
        var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(Assert.Single(document.Sections).Elements));
        Assert.Equal(expected, Text(table));
        AssertLiteralTable(table);
        Assert.Equal("t", Assert.IsType<JsonElement>(Assert.Single(table.Sources).GetRawRepresentation()).GetProperty("id").GetString());
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Fact]
    public async Task ExtractAsync_TableWithoutPayload_PreservesStructuralTableWithoutFabrication()
    {
        using var session = new OfflineSession(PageResponse("[t](t)", [new { id = "t" }]));
        ExtractionDocument document = await Extract(session);
        var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(Assert.Single(document.Sections).Elements));
        Assert.Null(table.Content);
        AssertLiteralTable(table);
        Assert.Single(table.Sources);
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("no marker", "| supplied |")]
    [InlineData("[t](t) repeated [t](t)", "| supplied |")]
    [InlineData("same twice same", "same")]
    public async Task ExtractAsync_UnplaceableRepeatedTables_AppendsWithoutInventedCells(string markdown, string content)
    {
        using var session = new OfflineSession(PageResponse(markdown, [new { id = "t", content }]));
        ExtractionDocument document = await Extract(session);
        ExtractionDocumentSection section = Assert.Single(document.Sections);
        Assert.Equal([markdown, content], section.Elements.Select(Text));
        var table = Assert.IsType<ExtractionDocumentTableElement>(section.Elements[1]);
        AssertLiteralTable(table);
        Assert.Equal(content, Assert.IsType<JsonElement>(Assert.Single(table.Sources).GetRawRepresentation()).GetProperty("content").GetString());
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Fact]
    public async Task ExtractAsync_OverlappingTables_AppendsAmbiguousTableWithoutLosingRemainder()
    {
        using var session = new OfflineSession(PageResponse("before abcdef after",
            [new { content = "abc" }, new { content = "abcdef" }]));
        ExtractionDocument document = await Extract(session);
        ExtractionDocumentSection section = Assert.Single(document.Sections);
        Assert.Equal(["before ", "abc", "def after", "abcdef"], section.Elements.Select(Text));
        Assert.IsType<ExtractionDocumentTextElement>(section.Elements[2]);
        Assert.All(section.Elements.OfType<ExtractionDocumentTableElement>(), AssertLiteralTable);
        Assert.Equal(2, section.Elements.OfType<ExtractionDocumentTableElement>().Count());
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("AAH/gA==")]
    [InlineData("data:image/png;base64,AAH/gA==")]
    public async Task ExtractAsync_PlainAndDataUriImageBase64_ReturnsExactNestedBytes(string base64)
    {
        using var session = new OfflineSession(PageResponse("image caption", [], [
            new { image_base64 = base64, top_left_x = 1.5, top_left_y = 2, bottom_right_x = 30, bottom_right_y = 40 }]));
        ExtractionDocument document = await Extract(session);
        ExtractionDocumentSection section = Assert.Single(document.Sections);
        Assert.Equal("image caption", Text(section.Elements[0]));
        var image = Assert.IsType<ExtractionDocumentImageElement>(section.Elements[1]);
        var data = Assert.IsType<DataContent>(image.Content);
        Assert.Equal([0, 1, 255, 128], data.Data.ToArray());
        Assert.Equal("image/png", data.MediaType);
        Assert.Null(image.Source);
        ExtractionDocumentSourceReference source = Assert.Single(image.Sources);
        Assert.Same(Assert.Single(document.SourcePages), source.Page);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Pixel, source.Page.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, source.Page.CoordinateOrigin);
        Assert.Equal(new ExtractionDocumentPageDimensions(640, 480), source.Page.Dimensions);
        Assert.Equal(new ExtractionDocumentPoint[] { new(1.5, 2), new(30, 2), new(30, 40), new(1.5, 40) }, source.Polygon);
        await session.AssertCleanAndBorrowedAsync(1, 1);
        Assert.Equal(base64, Assert.IsType<JsonElement>(source.GetRawRepresentation()).GetProperty("image_base64").GetString());
        Assert.Equal([0, 1, 255, 128], data.Data.ToArray());
    }

    [Fact]
    public async Task ExtractAsync_ImageWithoutBytesOrCompleteCoordinates_PreservesNativeWithoutInventingData()
    {
        using var session = new OfflineSession(PageResponse("", [], [new { id = "description-only", top_left_x = 7 }]));
        ExtractionDocument document = await Extract(session);
        var image = Assert.IsType<ExtractionDocumentImageElement>(Assert.Single(Assert.Single(document.Sections).Elements));
        Assert.Null(image.Content);
        ExtractionDocumentSourceReference source = Assert.Single(image.Sources);
        Assert.Empty(source.Polygon);
        Assert.Same(document.SourcePages[0], source.Page);
        Assert.Equal("description-only", Assert.IsType<JsonElement>(source.GetRawRepresentation()).GetProperty("id").GetString());
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Fact]
    public async Task ExtractAsync_PageIndicesAndDimensions_PreserveOriginalPageFrames()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            """{"pages":[{"index":7,"markdown":"eighth","dimensions":{"width":720,"height":900}},{"index":1,"markdown":"second"}]}""");
        using var session = new OfflineSession(bytes);
        ExtractionDocument document = await Extract(session);
        Assert.Equal([8, 2], document.SourcePages.Select(p => p.PageNumber));
        Assert.Equal(["eighth", "second"], document.EnumerateContent().Select(Text));
        Assert.Equal(new ExtractionDocumentPageDimensions(720, 900), document.SourcePages[0].Dimensions);
        Assert.Equal("invoice.pdf:foundry-mistral-ocr:page:8", document.SourcePages[0].Identifier);
        Assert.Null(document.SourcePages[1].Dimensions);
        Assert.Null(document.SourcePages[1].CoordinateUnit);
        Assert.Null(document.SourcePages[1].CoordinateOrigin);
        for (int i = 0; i < 2; i++)
        {
            Assert.Same(document.SourcePages[i], Assert.Single(document.Sections[i].Sources).Page);
            Assert.Same(document.SourceOutputs[0], document.SourcePages[i].SourceOutput);
            Assert.Same(document.SourcePages[i], Assert.Single(document.Sections[i].Elements[0].Sources).Page);
        }

        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-pages")]
    [InlineData("invalid-image")]
    [InlineData("duplicate-page")]
    [InlineData("negative-page")]
    [InlineData("invalid-dimensions")]
    public async Task ExtractAsync_MalformedJsonOrInvalidImage_PropagatesAndCleansUp(string scenario)
    {
        (string json, Type errorType) = scenario switch
        {
            "malformed" => ("{", typeof(JsonException)),
            "missing-pages" => ("""{"pages":{}}""", typeof(InvalidDataException)),
            "invalid-image" => ("""{"pages":[{"index":0,"images":[{"image_base64":"not base64!"}]}]}""", typeof(FormatException)),
            "duplicate-page" => ("""{"pages":[{"index":0},{"index":0}]}""", typeof(InvalidDataException)),
            "negative-page" => ("""{"pages":[{"index":-1}]}""", typeof(ArgumentOutOfRangeException)),
            _ => ("""{"pages":[{"index":0,"dimensions":{"width":0,"height":100}}]}""", typeof(ArgumentOutOfRangeException)),
        };
        using var session = new OfflineSession(Encoding.UTF8.GetBytes(json));
        Exception? error = await Record.ExceptionAsync(() => Extract(session));
        Assert.NotNull(error);
        Assert.IsAssignableFrom(errorType, error);
        Assert.Empty(session.Delays);
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Fact]
    public async Task ExtractAsync_503ThenSuccess_UsesExpectedDelaysAndFreshRequests()
    {
        using var session = new OfflineSession(Encoding.UTF8.GetBytes("retry-1"), HttpStatusCode.ServiceUnavailable);
        for (int i = 2; i <= 4; i++)
        {
            session.Handler.Replies.Enqueue((HttpStatusCode.ServiceUnavailable, Encoding.UTF8.GetBytes($"retry-{i}")));
        }

        session.Handler.Replies.Enqueue((HttpStatusCode.OK, s_fidelityResponse));
        ExtractionDocument document = await Extract(session, RequestedOptions());
        Assert.Equal([3d, 6d, 9d, 12d], session.Delays.Select(d => d.TotalSeconds));
        Assert.All(session.Handler.Requests, AssertRequest);
        AssertFreshRequests(session.Handler.Requests);
        ExtractionDocumentSourceOutput output = Assert.Single(document.SourceOutputs);
        Assert.Equal(s_fidelityResponse, output.GetRawContent()!.Content.ToArray());
        Assert.Equal("genuine-wire-model", output.ModelId);
        Assert.Equal(4, Assert.Single(document.Sections).Elements.Count);
        await session.AssertCleanAndBorrowedAsync(5, 5);
    }

    [Fact]
    public async Task ExtractAsync_Five503Responses_StopsAndDisposesAllAttempts()
    {
        using var session = new OfflineSession(Encoding.UTF8.GetBytes("unavailable"), HttpStatusCode.ServiceUnavailable);
        for (int i = 1; i < 5; i++)
        {
            session.Handler.Replies.Enqueue((HttpStatusCode.ServiceUnavailable, Encoding.UTF8.GetBytes("still unavailable")));
        }

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => Extract(session, RequestedOptions()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Contains("503", error.Message);
        Assert.Equal([3d, 6d, 9d, 12d], session.Delays.Select(d => d.TotalSeconds));
        Assert.All(session.Handler.Requests, AssertRequest);
        AssertFreshRequests(session.Handler.Requests);
        Assert.Empty(session.Handler.Replies);
        await session.AssertCleanAndBorrowedAsync(5, 5);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ExtractAsync_Non503Failure_DoesNotRetry(HttpStatusCode status)
    {
        using var session = new OfflineSession(Encoding.UTF8.GetBytes("failure"), status);
        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => Extract(session));
        Assert.Equal(status, error.StatusCode);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message);
        Assert.Empty(session.Delays);
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("pre")]
    [InlineData("copy")]
    [InlineData("handler")]
    [InlineData("retry")]
    public async Task ExtractAsync_CopySendOrRetryCancellation_PreservesTokenAndBorrowedResources(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        using var session = new OfflineSession(status: stage == "retry" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        if (stage == "pre")
        {
            cancellation.Cancel();
        }
        else if (stage == "copy")
        {
            session.Input.BeforeCopy = token =>
            {
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
            };
        }
        else if (stage == "handler")
        {
            session.Handler.OnSend = token =>
            {
                // HttpClient passes a linked token, not necessarily the original struct.
                Assert.True(token.CanBeCanceled);
                cancellation.Cancel();
                Assert.True(token.IsCancellationRequested);
            };
        }
        else
        {
            session.OnDelay = (delay, token) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(3), delay);
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
            };
        }

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Extract(session, cancellationToken: cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(error.CancellationToken.IsCancellationRequested);
        if (stage != "handler")
        {
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }

        Assert.Equal(stage == "retry" ? new[] { TimeSpan.FromSeconds(3) } : [], session.Delays);
        await session.AssertCleanAndBorrowedAsync(stage is "handler" or "retry" ? 1 : 0, stage == "retry" ? 1 : 0,
            stage is "handler" or "retry" ? 1 : 0);
    }

    [Fact]
    public async Task ExtractAsync_SnapshotsOptionsBeforeInputCopyAwait_PreservesRequestAndCapturePolicy()
    {
        var input = new GatedInput(OfflineSession.InputBytes);
        using var session = new OfflineSession(input: input);
        DocumentExtractionOptions options = RequestedOptions();
        Task<ExtractionDocument> pending = Extract(session, options);
        Assert.True(input.CopyEntered);
        Assert.False(pending.IsCompleted);
        Assert.Empty(session.Handler.Requests);
        options.ExtractorName = "mutated";
        options.CaptureRawContent = false;
        options.AdditionalProperties["imageLimit"] = 99;
        options.AdditionalProperties["unsupported-later"] = true;
        input.Release();
        ExtractionDocument document = await pending;
        AssertRequest(Assert.Single(session.Handler.Requests));
        Assert.Equal(ExtractionDocumentRawContentStatus.Retained, Assert.Single(document.SourceOutputs).ContentStatus);
        Assert.Equal(OfflineSession.EmptyResponse, document.SourceOutputs[0].GetRawContent()!.Content.ToArray());
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("wrong-case")]
    [InlineData("wrong-type")]
    [InlineData("negative-limit")]
    [InlineData("negative-size")]
    public async Task ExtractAsync_InvalidOptions_RejectsBeforeHttpOrTokenAcquisition(string scenario)
    {
        using var session = new OfflineSession();
        var options = new DocumentExtractionOptions();
        options.AdditionalProperties[scenario switch
        {
            "wrong-case" => "ImageLimit",
            "negative-size" => "imageMinSize",
            _ => "imageLimit",
        }] = scenario == "wrong-type" ? "two" : -1;
        Exception? error = await Record.ExceptionAsync(() => Extract(session, options));
        if (scenario == "wrong-case")
        {
            Assert.IsType<NotSupportedException>(error);
            Assert.Equal(0, session.Input.CopyCalls);
            Assert.Equal(0, session.Input.Position);
        }
        else if (scenario == "wrong-type")
        {
            Assert.IsType<ArgumentException>(error);
        }
        else
        {
            Assert.IsType<ArgumentOutOfRangeException>(error);
        }

        Assert.Empty(session.Delays);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public async Task ExtractAsync_ImageDefaultsAndZeroLimits_ReachWireWithoutMutatingOptions(bool defaultImages, bool? overrideImages, bool expected)
    {
        using var session = new OfflineSession(includeImages: defaultImages);
        var options = new DocumentExtractionOptions();
        if (overrideImages is not null)
        {
            options.AdditionalProperties["includeImages"] = overrideImages.Value;
        }

        options.AdditionalProperties["imageLimit"] = 0;
        options.AdditionalProperties["imageMinSize"] = 0;
        await Extract(session, options);
        using JsonDocument body = JsonDocument.Parse(Assert.Single(session.Handler.Requests).Body);
        Assert.Equal(expected, body.RootElement.GetProperty("include_image_base64").GetBoolean());
        Assert.Equal("default-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("image_limit").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("image_min_size").GetInt32());
        Assert.Null(options.ExtractorName);
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("before TABLE after", "TABLE", 7, 5)]
    [InlineData("table", "TABLE", null, null)]
    [InlineData("TABLE TABLE", "TABLE", null, null)]
    [InlineData("xyz", "absent", null, null)]
    [InlineData("abcabcdef", "abc", null, null)]
    public void FindUnique_UniqueMissingAndRepeatedMatches_ReturnsOnlyUnambiguousPosition(string source, string value, int? start, int? length)
    {
        Assert.Equal(start is null ? null : ((int Start, int Length)?)(start.Value, length!.Value),
            FoundryMistralOcrClient.FindUnique(source, value));
        Assert.Equal((0, 3), FoundryMistralOcrClient.FindUnique("ABCdef", "ABC"));
    }

    [Fact]
    public async Task GetService_SelfUnknownAndKeyedLookup_ReturnsContractServices()
    {
        using var session = new OfflineSession();
        Assert.Same(session.Client, session.Client.GetService(typeof(FoundryMistralOcrClient)));
        Assert.Same(session.Client, session.Client.GetService(typeof(IDocumentExtractionClient)));
        Assert.Null(session.Client.GetService(typeof(HttpClient)));
        Assert.Null(session.Client.GetService(typeof(IDocumentExtractionClient), "key"));
        Assert.Equal("serviceType", Assert.Throws<ArgumentNullException>(() => session.Client.GetService(null!)).ParamName);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    [Theory]
    [InlineData("endpoint-null", "endpoint")]
    [InlineData("credential-null", "credential")]
    [InlineData("delay-null", "delayAsync")]
    [InlineData("model-null", "defaultModel")]
    [InlineData("model-empty", "defaultModel")]
    [InlineData("model-whitespace", "defaultModel")]
    [InlineData("relative", "endpoint")]
    [InlineData("file", "endpoint")]
    public async Task Constructor_InvalidArguments_RejectsWithoutRealTokenAcquisition(string scenario, string parameter)
    {
        using var session = new OfflineSession();
        Uri? endpoint = scenario switch
        {
            "endpoint-null" => null,
            "relative" => new Uri("relative", UriKind.Relative),
            "file" => new Uri("file:///unused"),
            _ => new Uri("https://unit.invalid"),
        };
        string? model = scenario switch { "model-null" => null, "model-empty" => "", "model-whitespace" => " ", _ => "model" };
        ArgumentException error = Assert.ThrowsAny<ArgumentException>(() =>
            new FoundryMistralOcrClient(endpoint!, scenario == "credential-null" ? null! : session.Credential,
                model!, false, session.Http, scenario == "delay-null" ? null! : (_, _) => Task.CompletedTask));
        Assert.Equal(parameter, error.ParamName);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    [Fact]
    public async Task Dispose_RepeatedCalls_DoesNotDisposeBorrowedHttpClient()
    {
        using var session = new OfflineSession();
        using (var publicClient = new FoundryMistralOcrClient(new Uri("https://unit.invalid"), session.Credential,
            httpClient: session.Http))
        {
            ExtractionDocument document = await publicClient.ExtractAsync(session.Input, "public.pdf", "application/pdf");
            Assert.Equal("wire-model", Assert.Single(document.SourceOutputs).ModelId);
            using JsonDocument request = JsonDocument.Parse(Assert.Single(session.Handler.Requests).Body);
            Assert.Equal("mistral-ocr-4-0", request.RootElement.GetProperty("model").GetString());
            publicClient.Dispose();
            publicClient.Dispose();
        }

        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_ResponseBodyFailureOrCancellation_DisposesResponseWithoutMaskingPrimaryOutcome(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var session = new OfflineSession();
        var primary = new InvalidOperationException("offline response read failed");
        session.Handler.OnResponse = response => response.Payload.BeforeRead = token =>
        {
            Assert.Equal(cancellation.Token, token);
            if (cancel)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }

            throw primary;
        };
        if (cancel)
        {
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Extract(session, cancellationToken: cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Extract(session, cancellationToken: cancellation.Token));
            Assert.Same(primary, error);
        }

        Assert.Empty(session.Delays);
        await session.AssertCleanAndBorrowedAsync(1, 1);
    }

    [Theory]
    [InlineData("stream", "document")]
    [InlineData("identifier-null", "identifier")]
    [InlineData("identifier-empty", "identifier")]
    [InlineData("identifier-whitespace", "identifier")]
    [InlineData("media-null", "mediaType")]
    [InlineData("media-empty", "mediaType")]
    [InlineData("media-whitespace", "mediaType")]
    public async Task ExtractAsync_InvalidInput_RejectsBeforeCopyOrTokenAcquisition(string scenario, string parameter)
    {
        using var session = new OfflineSession();
        string? identifier = scenario switch
        {
            "identifier-null" => null,
            "identifier-empty" => "",
            "identifier-whitespace" => " ",
            _ => "invoice.pdf",
        };
        string? mediaType = scenario switch
        {
            "media-null" => null,
            "media-empty" => "",
            "media-whitespace" => " ",
            _ => "application/pdf",
        };
        ArgumentException error = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            session.Client.ExtractAsync(scenario == "stream" ? null! : session.Input, identifier!, mediaType!));
        Assert.Equal(parameter, error.ParamName);
        Assert.Equal(0, session.Input.CopyCalls);
        Assert.Equal(0, session.Input.Position);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    private static Task<ExtractionDocument> Extract(OfflineSession session, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
        => session.Client.ExtractAsync(session.Input, "invoice.pdf", "application/pdf", options, cancellationToken);

    private static string Text(ExtractionDocumentElement element) => Assert.IsType<TextContent>(element.Content).Text;

    private static void AssertLiteralTable(ExtractionDocumentTableElement table)
    {
        Assert.Equal(0, table.Cells.RowCount);
        Assert.Equal(0, table.Cells.ColumnCount);
        Assert.Empty(table.Cells);
        Assert.Empty(Assert.Single(table.Sources).Polygon);
    }

    private static DocumentExtractionOptions RequestedOptions()
    {
        var options = new DocumentExtractionOptions { ExtractorName = "genuine-request-model", CaptureRawContent = true };
        options.AdditionalProperties["includeImages"] = true;
        options.AdditionalProperties["imageLimit"] = 2;
        options.AdditionalProperties["imageMinSize"] = 40;
        return options;
    }

    private static void AssertRequest(RecordedRequest recorded)
    {
        Assert.Equal(HttpMethod.Post, recorded.Message.Method);
        Assert.Equal(new Uri("https://unit.invalid/base/providers/mistral/azure/ocr"), recorded.Message.RequestUri);
        Assert.Equal("Bearer", recorded.Scheme);
        Assert.Equal("local-offline-token", recorded.Token);
        Assert.Equal("application/json", recorded.Message.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", recorded.Message.Content.Headers.ContentType.CharSet);
        using JsonDocument request = JsonDocument.Parse(recorded.Body);
        JsonElement root = request.RootElement;
        Assert.Equal("genuine-request-model", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("include_image_base64").GetBoolean());
        Assert.Equal(2, root.GetProperty("image_limit").GetInt32());
        Assert.Equal(40, root.GetProperty("image_min_size").GetInt32());
        Assert.Equal("document_url", root.GetProperty("document").GetProperty("type").GetString());
        Assert.Equal("data:application/pdf;base64,ChQeAP8=", root.GetProperty("document").GetProperty("document_url").GetString());
    }

    private static void AssertFreshRequests(IReadOnlyList<RecordedRequest> requests)
    {
        Assert.Equal(5, requests.Count);
        for (int i = 0; i < requests.Count; i++)
        {
            for (int j = i + 1; j < requests.Count; j++)
            {
                Assert.NotSame(requests[i].Message, requests[j].Message);
                Assert.NotSame(requests[i].Message.Content, requests[j].Message.Content);
                Assert.Equal(requests[i].Body, requests[j].Body);
            }
        }
    }

    private static byte[] PageResponse(string markdown, object[] tables, object[]? images = null)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = "wire-table-model",
            pages = new[] { new { index = 0, markdown, dimensions = new { width = 640, height = 480 }, tables, images = images ?? [] } },
        });
}
