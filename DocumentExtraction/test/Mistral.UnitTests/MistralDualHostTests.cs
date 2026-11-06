// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using System.Text.Json;

using Azure.Core;

using CommunityToolkit.DocumentExtraction.Mistral.UnitTests.TestSupport;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.Mistral.UnitTests;

public sealed partial class MistralDocumentExtractionClientTests
{
    public static IEnumerable<object[]> NativeRequestCases()
    {
        string[] endpoints =
        [
            "https://api.mistral.ai/v1/ocr",
            "https://deployment.invalid/v1/ocr",
            "https://resource.invalid/providers/mistral/azure/ocr?api-version=example",
            "https://gateway.invalid/custom/ocr/",
        ];
        string[] mediaTypes =
        [
            "application/pdf", "image/png", "image/jpeg", "image/avif", "IMAGE/PNG",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ];
        foreach (bool useApiKey in new[] { false, true })
        {
            foreach (string endpoint in endpoints)
            {
                foreach (string mediaType in mediaTypes)
                {
                    yield return [useApiKey, endpoint, mediaType];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(NativeRequestCases))]
    public async Task ExtractAsync_ExplicitEndpointAndCredential_UseNativeUnionAndSharedGraph(
        bool useApiKey, string endpoint, string mediaType)
    {
        using var session = new OfflineSession(s_fidelityResponse, ocrEndpoint: new Uri(endpoint),
            useApiKey: useApiKey, tokenScope: "https://custom-audience.invalid/.default");
        ExtractionDocument document = await session.Client.ExtractAsync(
            session.Input, "invoice", mediaType, RequestedOptions());

        RecordedRequest request = Assert.Single(session.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Message.Method);
        Assert.Equal(new Uri(endpoint), request.Message.RequestUri);
        Assert.Equal("Bearer", request.Scheme);
        Assert.Equal(useApiKey ? "local-api-key" : "local-offline-token", request.Token);
        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.Equal("genuine-request-model", body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("include_image_base64").GetBoolean());
        Assert.Equal(2, body.RootElement.GetProperty("image_limit").GetInt32());
        Assert.Equal(40, body.RootElement.GetProperty("image_min_size").GetInt32());
        JsonElement input = body.RootElement.GetProperty("document");
        string union = mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image_url" : "document_url";
        Assert.Equal(union, input.GetProperty("type").GetString());
        Assert.Equal($"data:{mediaType};base64,ChQeAP8=", input.GetProperty(union).GetString());
        Assert.Equal(2, input.EnumerateObject().Count());

        ExtractionDocumentSection section = Assert.Single(document.Sections);
        Assert.Equal(["before\n", "<table><tr><th colspan=\"2\">Invoice</th></tr><tr><td></td><td>17.50</td></tr></table>", "\nafter Ω"],
            section.Elements.Where(element => element.Content is TextContent).Select(Text));
        AssertLiteralTable(Assert.IsType<ExtractionDocumentTableElement>(section.Elements[1]));
        var image = Assert.IsType<ExtractionDocumentImageElement>(section.Elements[3]);
        Assert.Equal([0, 1, 255, 128], Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Equal(5, Assert.Single(document.SourcePages).PageNumber);
        Assert.Equal("invoice:mistral-ocr:page:5", document.SourcePages[0].Identifier);
        Assert.All(section.Elements, element => Assert.Same(document.SourcePages[0], Assert.Single(element.Sources).Page));
        await session.AssertCleanAndBorrowedAsync(1, 1, useApiKey ? 0 : 1);
        Assert.Equal("genuine-wire-model", Assert.IsType<JsonElement>(document.RawRepresentation).GetProperty("model").GetString());
        Assert.Equal("img-7", Assert.IsType<JsonElement>(Assert.Single(image.Sources).GetRawRepresentation()).GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_PublicOptionsConstructor_UsesExactUriDefaultModelAndBorrowedResources(bool useApiKey)
    {
        using var session = new OfflineSession();
        var options = new MistralDocumentExtractionClientOptions
        {
            OcrEndpoint = new Uri("https://configured.invalid/v1/ocr?api-version=example"),
            DefaultModel = "configured-model",
            IncludeImages = true,
        };
        using (var client = useApiKey
            ? new MistralDocumentExtractionClient(options, "public-api-key", session.Http)
            : new MistralDocumentExtractionClient(options, session.Credential, session.Http))
        {
            ExtractionDocument document = await client.ExtractAsync(session.Input, "input.pdf", "application/pdf");
            Assert.Empty(document.Sections);
        }

        RecordedRequest request = Assert.Single(session.Handler.Requests);
        Assert.Equal(options.OcrEndpoint, request.Message.RequestUri);
        Assert.Equal("Bearer", request.Scheme);
        Assert.Equal(useApiKey ? "public-api-key" : "local-offline-token", request.Token);
        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.Equal("configured-model", body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("include_image_base64").GetBoolean());
        await session.AssertCleanAndBorrowedAsync(1, 1, useApiKey ? 0 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_Retry_ReacquiresCredentialAndRetainsExactEndpointAndRequest(bool useApiKey)
    {
        var endpoint = new Uri("https://configured.invalid/v1/ocr");
        using var session = new OfflineSession(status: HttpStatusCode.ServiceUnavailable,
            ocrEndpoint: endpoint, useApiKey: useApiKey);
        session.Handler.Replies.Enqueue((HttpStatusCode.OK, s_fidelityResponse));
        session.Credential.Tokens.Enqueue(new AccessToken("first-token", DateTimeOffset.UtcNow.AddSeconds(1)));
        session.Credential.Tokens.Enqueue(new AccessToken("refreshed-token", DateTimeOffset.MaxValue));

        ExtractionDocument document = await Extract(session);
        Assert.Single(document.Sections);
        Assert.Equal([TimeSpan.FromSeconds(3)], session.Delays);
        Assert.Equal(useApiKey ? ["local-api-key", "local-api-key"] : new[] { "first-token", "refreshed-token" },
            session.Handler.Requests.Select(request => request.Token));
        Assert.All(session.Handler.Requests, request => Assert.Equal(endpoint, request.Message.RequestUri));
        Assert.Equal(session.Handler.Requests[0].Body, session.Handler.Requests[1].Body);
        Assert.NotSame(session.Handler.Requests[0].Message, session.Handler.Requests[1].Message);
        await session.AssertCleanAndBorrowedAsync(2, 2, useApiKey ? 0 : 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_AuthenticationFailure_DoesNotTryAnotherHostOrCredential(bool useApiKey)
    {
        var endpoint = new Uri("https://configured.invalid/v1/ocr");
        using var session = new OfflineSession(status: HttpStatusCode.Unauthorized,
            ocrEndpoint: endpoint, useApiKey: useApiKey);
        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => Extract(session));
#if STANDARD_PROVIDER_ASSET
        Assert.Null(error.StatusCode);
#else
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
#endif
        Assert.Contains("401", error.Message);
        Assert.Equal(endpoint, Assert.Single(session.Handler.Requests).Message.RequestUri);
        Assert.Empty(session.Delays);
        await session.AssertCleanAndBorrowedAsync(1, 1, useApiKey ? 0 : 1);
    }

    [Theory]
    [InlineData(false, "copy")]
    [InlineData(true, "copy")]
    [InlineData(false, "send")]
    [InlineData(true, "send")]
    [InlineData(false, "retry")]
    [InlineData(true, "retry")]
    [InlineData(false, "read")]
    [InlineData(true, "read")]
    public async Task ExtractAsync_ExplicitEndpointCancellation_CleansUpAndPreservesBorrowedInput(bool useApiKey, string stage)
    {
        using var cancellation = new CancellationTokenSource();
        using var session = new OfflineSession(status: stage == "retry" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            ocrEndpoint: new Uri("https://configured.invalid/v1/ocr"), useApiKey: useApiKey);
        if (stage == "copy")
        {
            session.Input.BeforeCopy = _ => cancellation.Cancel();
        }
        else if (stage == "send")
        {
            session.Handler.OnSend = _ => cancellation.Cancel();
        }
        else if (stage == "read")
        {
            session.Handler.OnResponse = response => response.Payload.BeforeRead = _ => cancellation.Cancel();
        }
        else
        {
            session.OnDelay = (_, _) => cancellation.Cancel();
        }

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Extract(session, cancellationToken: cancellation.Token));
        Assert.True(error.CancellationToken.IsCancellationRequested);
        await session.AssertCleanAndBorrowedAsync(stage == "copy" ? 0 : 1, stage is "copy" or "send" ? 0 : 1,
            useApiKey || stage == "copy" ? 0 : 1);
    }

    [Fact]
    public async Task ExtractAsync_TokenCancellationAndFailure_NeverSendHttp()
    {
        using var session = new OfflineSession(ocrEndpoint: new Uri("https://configured.invalid/v1/ocr"));
        var failure = new InvalidOperationException("credential failure");
        session.Credential.BeforeGetToken = _ => throw failure;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Extract(session)));
        session.Input.Position = 0;
        using var cancellation = new CancellationTokenSource();
        session.Credential.BeforeGetToken = _ => cancellation.Cancel();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Extract(session, cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad key")]
    [InlineData("bad\r\nkey")]
    public void Constructor_InvalidApiKey_FailsWithoutAcquisition(string? key)
    {
        var options = new MistralDocumentExtractionClientOptions
        {
            OcrEndpoint = new Uri("https://api.mistral.ai/v1/ocr"),
            DefaultModel = "model",
        };
        Assert.Equal("apiKey", Assert.ThrowsAny<ArgumentException>(() => new MistralDocumentExtractionClient(options, key!)).ParamName);
    }

    [Theory]
    [InlineData("null-endpoint", "OcrEndpoint")]
    [InlineData("relative", "OcrEndpoint")]
    [InlineData("file", "OcrEndpoint")]
    [InlineData("userinfo", "OcrEndpoint")]
    [InlineData("fragment", "OcrEndpoint")]
    [InlineData("model", "DefaultModel")]
    [InlineData("scope", "TokenScope")]
    public async Task Constructor_InvalidExplicitOptions_FailsBeforeTokenOrHttp(string scenario, string parameter)
    {
        using var session = new OfflineSession();
        var options = new MistralDocumentExtractionClientOptions
        {
            OcrEndpoint = scenario switch
            {
                "null-endpoint" => null!,
                "relative" => new Uri("relative", UriKind.Relative),
                "file" => new Uri("file:///unused"),
                "userinfo" => new Uri("https://name:password@unit.invalid/v1/ocr"),
                "fragment" => new Uri("https://unit.invalid/v1/ocr#fragment"),
                _ => new Uri("https://unit.invalid/v1/ocr"),
            },
            DefaultModel = scenario == "model" ? " " : "model",
            TokenScope = scenario == "scope" ? " " : "https://cognitiveservices.azure.com/.default",
        };
        Assert.Equal(parameter, Assert.ThrowsAny<ArgumentException>(() =>
            new MistralDocumentExtractionClient(options, session.Credential, session.Http)).ParamName);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    [Fact]
    public async Task Constructor_NullOptionsOrTokenCredential_FailsBeforeAcquisition()
    {
        using var session = new OfflineSession();
        var options = new MistralDocumentExtractionClientOptions
        {
            OcrEndpoint = new Uri("https://unit.invalid/v1/ocr"),
            DefaultModel = "model",
        };
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() =>
            new MistralDocumentExtractionClient(null!, "key", session.Http)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() =>
            new MistralDocumentExtractionClient(null!, session.Credential, session.Http)).ParamName);
        Assert.Equal("credential", Assert.Throws<ArgumentNullException>(() =>
            new MistralDocumentExtractionClient(options, (TokenCredential)null!, session.Http)).ParamName);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    [Theory]
    [InlineData("https://unit.invalid/base?query=value")]
    [InlineData("https://unit.invalid/base#fragment")]
    [InlineData("https://name:password@unit.invalid/base")]
    public async Task Constructor_InvalidFoundryBaseEndpoint_RejectsAmbiguousOrCredentialedUri(string endpoint)
    {
        using var session = new OfflineSession();
        Assert.Equal("endpoint", Assert.Throws<ArgumentException>(() =>
            new MistralDocumentExtractionClient(new Uri(endpoint), session.Credential, httpClient: session.Http)).ParamName);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }

    [Theory]
    [InlineData("not a media type")]
    [InlineData("image/*")]
    [InlineData("*/*")]
    public async Task ExtractAsync_InvalidMediaType_FailsBeforeCopyOrHttp(string mediaType)
    {
        using var session = new OfflineSession(useApiKey: true);
        Assert.Equal("mediaType", (await Assert.ThrowsAsync<ArgumentException>(() =>
            session.Client.ExtractAsync(session.Input, "input", mediaType))).ParamName);
        Assert.Equal(0, session.Input.CopyCalls);
        await session.AssertCleanAndBorrowedAsync(0, 0, 0);
    }
}
