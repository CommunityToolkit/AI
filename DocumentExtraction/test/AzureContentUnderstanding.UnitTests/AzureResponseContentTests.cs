using Microsoft.Extensions.DocumentExtraction;
using CommunityToolkit.DocumentExtraction.Tests;

namespace CommunityToolkit.DocumentExtraction.AzureContentUnderstanding.UnitTests;

public sealed class AzureResponseContentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_SeekableBody_RetainsExactBytesIncludingEmptyAndRestoresOriginalPosition(bool empty)
    {
        byte[] bytes = empty ? [] : [32, 10, 123, 34, 120, 34, 58, 49, 125, 9];
        using var response = new TrackedResponse(bytes);
        response.ContentStream!.Position = empty ? 0 : 4;
        long original = response.ContentStream.Position;
        byte[]? captured = AzureResponseContent.GetBufferedResponseBytes(response);
        Assert.Equal(bytes, captured);
        Assert.Equal(original, response.ContentStream.Position);
        var output = ProviderAdapterHelpers.CreateSourceOutput("id", "test", null,
            new DocumentExtractionOptions { CaptureRawContent = true }, null, captured);
        Assert.Equal("Retained", output.ContentStatus.ToString());
        Assert.Equal("application/json", output.GetRawContent()!.MediaType);
        Assert.Equal(bytes, output.GetRawContent()!.Content.ToArray());
        Assert.Equal(0, response.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_SeekableReadFailureOrCancellation_RestoresPositionInFinally(bool cancelled)
    {
        Exception failure = cancelled ? new OperationCanceledException("cancelled capture") : new IOException("broken capture");
        using var stream = new FaultingBody([1, 2, 3, 4, 5], failure) { Position = 3 };
        using var response = new TrackedResponse([]) { ContentStream = stream };
        Exception? actual = Record.Exception(() => AzureResponseContent.GetBufferedResponseBytes(response));
        Assert.Same(failure, actual);
        Assert.Equal(3, stream.Position);
        Assert.Equal(1, stream.Reads);
        Assert.False(stream.Disposed);
        Assert.Equal(0, response.DisposeCount);
    }

    [Fact]
    public void Capture_NonseekableBody_ReportsUnavailableWithoutFabricatingBytes()
    {
        using var body = new FaultingBody([1, 2, 3], new IOException("must not read"), seekable: false);
        using var response = new TrackedResponse([]) { ContentStream = body };
        byte[]? bytes = AzureResponseContent.GetBufferedResponseBytes(response);
        var output = ProviderAdapterHelpers.CreateSourceOutput("id", "test", null,
            new DocumentExtractionOptions { CaptureRawContent = true }, null, bytes);
        Assert.Null(bytes);
        Assert.Equal("Unavailable", output.ContentStatus.ToString());
        Assert.Null(output.GetRawContent());
        Assert.Equal(0, body.Reads);
        Assert.False(body.Disposed);
    }
}
