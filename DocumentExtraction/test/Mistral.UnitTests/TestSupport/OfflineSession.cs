// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;

using Azure.Core;

namespace CommunityToolkit.DocumentExtraction.Mistral.UnitTests.TestSupport;

internal sealed class FixedCredential : TokenCredential, IDisposable
{
    public int Calls { get; private set; }
    public List<string[]> Scopes { get; } = [];
    public Queue<AccessToken> Tokens { get; } = [];
    public Action<CancellationToken>? BeforeGetToken { get; set; }
    public int DisposeCalls { get; private set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeGetToken?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        Scopes.Add(requestContext.Scopes.ToArray());
        return Tokens.TryDequeue(out AccessToken token)
            ? token
            : new AccessToken("local-offline-token", DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(GetToken(requestContext, cancellationToken));

    public void Dispose() => DisposeCalls++;
}

internal sealed class TrackingContent(byte[] bytes) : HttpContent
{
    public int DisposeCalls { get; private set; }
    public Action<CancellationToken>? BeforeRead { get; set; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        BeforeRead?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await stream.WriteAsync(bytes, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = bytes.Length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalls++;
        }

        base.Dispose(disposing);
    }
}

internal sealed class TrackingResponse(HttpStatusCode status, byte[] bytes) : HttpResponseMessage(status)
{
    public TrackingContent Payload { get; } = new(bytes);
    public int DisposeCalls { get; private set; }

    public void AttachPayload() => Content = Payload;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalls++;
        }

        base.Dispose(disposing);
    }
}

internal sealed record RecordedRequest(
    HttpRequestMessage Message, byte[] Body, string? Scheme, string? Token, CancellationToken CancellationToken);

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    public Queue<(HttpStatusCode Status, byte[] Body)> Replies { get; } = [];
    public List<RecordedRequest> Requests { get; } = [];
    public List<TrackingResponse> Responses { get; } = [];
    public Action<CancellationToken>? OnSend { get; set; }
    public Action<TrackingResponse>? OnResponse { get; set; }
    public int DisposeCalls { get; private set; }
    public int ProbeCalls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A post-disposal probe never goes through DNS or a socket.
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/borrowed-probe")
        {
            ProbeCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("still usable") };
        }

        Assert.NotEmpty(Replies);
        byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new(request, body, request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter, cancellationToken));
        OnSend?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        (HttpStatusCode status, byte[] bytes) = Replies.Dequeue();
        var response = new TrackingResponse(status, bytes);
        response.AttachPayload();
        Responses.Add(response);
        OnResponse?.Invoke(response);
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalls++;
        }

        base.Dispose(disposing);
    }
}

internal class TrackingInput(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public int DisposeCalls { get; private set; }
    public int CopyCalls { get; private set; }
    public Action<CancellationToken>? BeforeCopy { get; set; }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        CopyCalls++;
        BeforeCopy?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return base.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalls++;
        }

        base.Dispose(disposing);
    }
}

internal sealed class GatedInput(byte[] bytes) : TrackingInput(bytes)
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool CopyEntered { get; private set; }
    public void Release() => _release.SetResult();

    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        CopyEntered = true;
        await _release.Task.WaitAsync(cancellationToken);
        await base.CopyToAsync(destination, bufferSize, cancellationToken);
    }
}

internal sealed class OfflineSession : IDisposable
{
    public static readonly byte[] InputBytes = [10, 20, 30, 0, 255];
    public static readonly byte[] EmptyResponse = Encoding.UTF8.GetBytes("""{ "model": "wire-model", "pages": [] }""" + "\n");
    public RecordingHttpMessageHandler Handler { get; } = new();
    public FixedCredential Credential { get; } = new();
    public HttpClient Http { get; }
    public MistralDocumentExtractionClient Client { get; }
    public TrackingInput Input { get; }
    public List<TimeSpan> Delays { get; } = [];
    public Action<TimeSpan, CancellationToken>? OnDelay { get; set; }
    private readonly string _tokenScope;

    public OfflineSession(byte[]? response = null, HttpStatusCode status = HttpStatusCode.OK,
        TrackingInput? input = null, bool includeImages = false, Uri? ocrEndpoint = null,
        bool useApiKey = false, string tokenScope = "https://cognitiveservices.azure.com/.default")
    {
        Handler.Replies.Enqueue((status, response ?? EmptyResponse));
        Http = new HttpClient(Handler);
        Input = input ?? new TrackingInput(InputBytes);
        _tokenScope = tokenScope;
        Client = ocrEndpoint is null && !useApiKey
            ? new MistralDocumentExtractionClient(new Uri("https://unit.invalid/base/"), Credential,
                "default-model", includeImages, Http, DelayAsync)
            : new MistralDocumentExtractionClient(new()
            {
                OcrEndpoint = ocrEndpoint ?? new Uri("https://api.mistral.ai/v1/ocr"),
                DefaultModel = "default-model",
                IncludeImages = includeImages,
                TokenScope = tokenScope,
            }, useApiKey ? null : Credential, useApiKey ? "local-api-key" : null, Http, DelayAsync);

        Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            Delays.Add(delay);
            OnDelay?.Invoke(delay, token);
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    public async Task AssertCleanAndBorrowedAsync(int requests, int responses, int credentialCalls = 1)
    {
        Assert.Equal(requests, Handler.Requests.Count);
        Assert.Equal(responses, Handler.Responses.Count);
        Assert.Equal(credentialCalls, Credential.Calls);
        Assert.All(Credential.Scopes, scopes =>
            Assert.Equal([_tokenScope], scopes));
        foreach (RecordedRequest request in Handler.Requests)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => request.Message.Content!.ReadAsByteArrayAsync());
        }

        foreach (TrackingResponse response in Handler.Responses)
        {
            Assert.Equal(1, response.DisposeCalls);
            Assert.Equal(1, response.Payload.DisposeCalls);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => response.Payload.ReadAsByteArrayAsync());
        }

        Client.Dispose();
        Client.Dispose();
        Assert.Equal(0, Handler.DisposeCalls);
        Assert.Equal(0, Credential.DisposeCalls);
        Assert.Equal(0, Input.DisposeCalls);
        Input.Position = 0;
        Assert.Equal(10, Input.ReadByte());
        using var probe = new HttpRequestMessage(HttpMethod.Get, "https://unit.invalid/borrowed-probe");
        using HttpResponseMessage reply = await Http.SendAsync(probe);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Equal("still usable", await reply.Content.ReadAsStringAsync());
        Assert.Equal(1, Handler.ProbeCalls);
    }

    public void Dispose()
    {
        Client.Dispose();
        Http.Dispose();
        Assert.Equal(1, Handler.DisposeCalls);
        Input.Dispose();
        Assert.Equal(1, Input.DisposeCalls);
    }
}
