using Azure;
using Azure.Core;
using Azure.Core.Pipeline;

namespace CommunityToolkit.DocumentExtraction.Tests;

internal sealed class FixedCredential : TokenCredential
{
    public int Calls { get; private set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return new AccessToken("offline-token", DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}

internal sealed class OfflineTransport(params TrackedResponse[] responses) : HttpPipelineTransport
{
    private static readonly HttpClientTransport RequestFactory = new();
    private readonly Queue<TrackedResponse> _responses = new(responses);

    public List<(RequestMethod Method, Uri Uri, CancellationToken Token)> Requests { get; } = [];
    public List<TrackedResponse> Delivered { get; } = [];
    public Action<HttpMessage, int>? AfterResponse { get; set; }

    public override Request CreateRequest() => RequestFactory.CreateRequest();

    public override void Process(HttpMessage message)
    {
        message.CancellationToken.ThrowIfCancellationRequested();
        Uri uri = message.Request.Uri.ToUri();
        Assert.Equal("offline.test", uri.Host);
        Assert.DoesNotContain("token", uri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Requests.Add((message.Request.Method, uri, message.CancellationToken));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Unexpected SDK request: " + uri);
        }

        TrackedResponse response = _responses.Dequeue();
        Delivered.Add(response);
        message.Response = response;
        AfterResponse?.Invoke(message, Requests.Count);
    }

    public override ValueTask ProcessAsync(HttpMessage message)
    {
        Process(message);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TrackedResponse : Response
{
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public TrackedResponse(byte[] bytes, int status = 200)
    {
        Status = status;
        ContentStream = new MemoryStream(bytes, writable: false);
        _headers["content-type"] = "application/json";
        _headers["retry-after"] = "0";
    }

    public static TrackedResponse Accepted()
    {
        var response = new TrackedResponse([], 202);
        response._headers["operation-location"] = AzureWireFixture.IsContentUnderstanding
            ? "https://offline.test/operations/receipt"
            : "https://offline.test/documentintelligence/documentModels/prebuilt-layout/analyzeResults/00000000-0000-0000-0000-000000000001?api-version=2024-11-30";
        return response;
    }

    public int DisposeCount { get; private set; }
    public override int Status { get; }
    public override string ReasonPhrase => Status < 400 ? "OK" : "Error";
    public override string ClientRequestId { get; set; } = "";
    public override Stream? ContentStream { get; set; }

    public override void Dispose()
    {
        DisposeCount++;
        // Azure LRO processing can release the underlying HTTP response before
        // consuming its buffered body. Keep the managed buffer readable, as
        // the SDK's buffered transport responses do; track response ownership.
    }

    protected override bool ContainsHeader(string name) => _headers.ContainsKey(name);
    protected override IEnumerable<HttpHeader> EnumerateHeaders() => _headers.Select(pair => new HttpHeader(pair.Key, pair.Value));
    protected override bool TryGetHeader(string name, out string value) => _headers.TryGetValue(name, out value!);
    protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
    {
        bool found = _headers.TryGetValue(name, out string? value);
        values = found ? [value!] : [];
        return found;
    }
}

internal sealed class FaultingBody(byte[] bytes, Exception failure, bool seekable = true) : MemoryStream(bytes)
{
    public int Reads { get; private set; }
    public bool Disposed { get; private set; }
    public override bool CanSeek => seekable;
    public override int Read(byte[] buffer, int offset, int count)
    {
        Reads++;
        base.Read(buffer, offset, Math.Min(count, 1));
        throw failure;
    }

    public override int Read(Span<byte> buffer)
    {
        Reads++;
        throw failure;
    }

    public override void CopyTo(Stream destination, int bufferSize)
    {
        Reads++;
        Position++;
        throw failure;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
