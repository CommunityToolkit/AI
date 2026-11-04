using CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using SkiaSharp;

namespace CommunityToolkit.DocumentExtraction.Vision.UnitTests;

public sealed class VisionFreeformAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_NamedImage_PreservesLiteralTextImageBytesExactResponseRootAndNativeSource(bool explicitRaw)
    {
        const string literal = "# Invoice Ω\n\n| Item | EUR |\n|---|---|\n| Tea | 12.50 |\n";
        object sentinel = new();
        var response = RecordingChatClient.Response(literal, explicitRaw ? sentinel : null);
        var chat = new RecordingChatClient(response);
        using var input = new BorrowedInput([137, 80, 78, 71, 1, 5, 9]);
        using var client = new VisionLlmOcrClient(chat, prompt: "Literal transcription, please.");
        var options = new DocumentExtractionOptions { ExtractorName = "requested-route" };
        var result = await client.ExtractAsync(input, "invoice.scan.png", "IMAGE/PNG", options);

        Assert.Equal("invoice.scan.png", result.Identifier);
        var section = Assert.Single(result.Sections);
        var text = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(section.Elements));
        Assert.Equal(literal, Assert.IsType<TextContent>(text.Content).Text);
        Assert.Null(result.Content);
        Assert.Null(section.Content);
        Assert.Null(text.Role);
        var source = Assert.Single(text.Sources);
        var page = Assert.Single(result.SourcePages);
        Assert.Same(page, source.Page);
        Assert.Same(source, Assert.Single(section.Sources));
        Assert.Null(page.PageNumber);
        Assert.Null(page.Dimensions);
        Assert.Null(page.CoordinateUnit);
        Assert.Null(page.CoordinateOrigin);
        Assert.Empty(source.Polygon);
        var nativeResponse = Assert.IsType<ChatResponse>(result.RawRepresentation);
        Assert.Same(response, nativeResponse);
        Assert.Equal("actual-vision-model", nativeResponse.ModelId);
        Assert.Equal(literal, nativeResponse.Text);
        Assert.Same(explicitRaw ? sentinel : response, source.GetRawRepresentation());
        Assert.Same(explicitRaw ? sentinel : null, nativeResponse.RawRepresentation);

        var request = Assert.Single(chat.Requests);
        var message = Assert.Single(request.Messages);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal(2, message.Contents.Count);
        Assert.Equal("Literal transcription, please.", Assert.IsType<TextContent>(message.Contents[0]).Text);
        var image = Assert.IsType<DataContent>(message.Contents[1]);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 1, 5, 9 }, image.Data.ToArray());
        Assert.Equal("IMAGE/PNG", image.MediaType);
        Assert.Equal("requested-route", request.Options!.ModelId);
        Assert.Null(request.Options.ResponseFormat);
        client.Dispose();
        input.AssertStillReadable(137);
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    public async Task ExtractAsync_EmptyOrWhitespaceText_DoesNotInventContent(string literal)
    {
        var response = RecordingChatClient.Response(literal, model: null);
        var chat = new RecordingChatClient(response);
        using var client = new VisionLlmOcrClient(chat);
        using var input = new BorrowedInput([1, 2, 3]);
        var result = await client.ExtractAsync(input, "empty.png", "image/png", new() { ExtractorName = "not-a-returned-model" });
        var section = Assert.Single(result.Sections);
        if (literal.Length == 0)
        {
            Assert.Empty(section.Elements);
        }
        else
        {
            Assert.Equal(literal, Assert.IsType<TextContent>(Assert.Single(section.Elements).Content).Text);
        }

        Assert.Same(response, result.RawRepresentation);
        Assert.Null(Assert.IsType<ChatResponse>(result.RawRepresentation).ModelId);
        Assert.Null(Assert.Single(result.SourcePages).PageNumber);
        Assert.Contains("GitHub-flavored Markdown", Assert.IsType<TextContent>(chat.Requests[0].Messages[0].Contents[0]).Text);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task ExtractAsync_ChatFailure_PropagatesWithoutRetryAndBorrowsResources()
    {
        var failure = new InvalidOperationException("deterministic chat failure");
        var chat = new RecordingChatClient(failure, RecordingChatClient.Response("forbidden fallback"));
        using var input = new BorrowedInput([4, 5, 6]);
        var client = new VisionLlmOcrClient(chat);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ExtractAsync(input, "fail.jpg", "image/jpeg")));
        Assert.Single(chat.Requests);
        Assert.Null(chat.Requests[0].Options!.ResponseFormat);
        client.Dispose();
        client.Dispose();
        input.AssertStillReadable(4);
        Assert.Equal(0, chat.DisposeCalls);
        Assert.Equal("forbidden fallback", (await chat.GetResponseAsync([])).Text);
    }

    [Theory]
    [InlineData("before-copy", 0)]
    [InlineData("during-copy", 0)]
    [InlineData("during-chat", 1)]
    public async Task ExtractAsync_Cancellation_PreservesTokenAndBorrowedResources(string stage, int calls)
    {
        using var cancellation = new CancellationTokenSource();
        var chat = new RecordingChatClient(RecordingChatClient.Response("must not return"))
        {
            OnRequest = stage == "during-chat" ? _ => cancellation.Cancel() : null,
        };
        using var input = new BorrowedInput([8, 9, 10], stage == "during-copy" ? cancellation : null);
        var client = new VisionLlmOcrClient(chat);
        if (stage == "before-copy")
        {
            cancellation.Cancel();
        }

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExtractAsync(input, "cancel.png", "image/png", cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(calls, chat.Requests.Count);
        if (calls == 1)
        {
            Assert.Equal(cancellation.Token, chat.Requests[0].Token);
        }

        client.Dispose();
        input.AssertStillReadable(8);
        Assert.Equal(0, chat.DisposeCalls);
        Assert.Equal("must not return", (await chat.GetResponseAsync([])).Text);
    }

    [Theory]
    [InlineData("media")]
    [InlineData("identifier")]
    [InlineData("null-stream")]
    [InlineData("unreadable")]
    [InlineData("option")]
    public async Task ExtractAsync_InvalidInput_RejectsBeforeChat(string scenario)
    {
        var chat = new RecordingChatClient(RecordingChatClient.Response("unused"));
        using var input = new MemoryStream([1, 2, 3]);
        using var client = new VisionLlmOcrClient(chat);
        if (scenario == "unreadable")
        {
            input.Dispose();
        }

        var options = new DocumentExtractionOptions();
        if (scenario == "option")
        {
            options.AdditionalProperties["temperature"] = 0;
        }

        var error = await Record.ExceptionAsync(() => client.ExtractAsync(
            scenario == "null-stream" ? null! : input,
            scenario == "identifier" ? " " : "input",
            scenario == "media" ? "text/plain" : "image/png", options));
        if (scenario is "media" or "option")
        {
            Assert.IsType<NotSupportedException>(error);
        }
        else if (scenario == "null-stream")
        {
            Assert.Equal("document", Assert.IsType<ArgumentNullException>(error).ParamName);
        }
        else
        {
            Assert.Equal(scenario == "identifier" ? "identifier" : "document", Assert.IsType<ArgumentException>(error).ParamName);
        }

        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task ExtractAsync_LocalPdf_SendsNativePngPerOriginalPageAndPreservesFrames()
    {
        ChatResponse[] responses =
        [
            RecordingChatClient.Response("# First\n42", new object()),
            RecordingChatClient.Response(""),
            RecordingChatClient.Response("Red square", new object()),
        ];
        var chat = new RecordingChatClient(responses);
        using var input = new BorrowedInput(PdfFixture.Create());
        using var client = new VisionLlmOcrClient(chat);
        var result = await client.ExtractAsync(input, "native.pdf", "APPLICATION/PDF");
        Assert.Equal(3, chat.Requests.Count);
        Assert.Equal(new int?[] { 1, 2, 3 }, result.SourcePages.Select(p => p.PageNumber));
        Assert.Equal(new[] { (300d, 400d), (500d, 600d), (700d, 800d) },
            result.SourcePages.Select(p => (p.Dimensions!.Value.Width, p.Dimensions.Value.Height)));
        Assert.Equal(new[] { "# First\n42", "Red square" },
            result.EnumerateContent().Select(e => Assert.IsType<TextContent>(e.Content).Text));
        Assert.Empty(result.Sections[1].Elements);
        var nativeResponses = Assert.IsType<ChatResponse[]>(result.RawRepresentation);
        Assert.Equal(3, nativeResponses.Length);
        for (int i = 0; i < 3; i++)
        {
            var page = result.SourcePages[i];
            Assert.Same(responses[i], nativeResponses[i]);
            var source = Assert.Single(result.Sections[i].Sources);
            Assert.Same(page, source.Page);
            Assert.Same(responses[i].RawRepresentation ?? responses[i], source.GetRawRepresentation());
            Assert.Equal(ExtractionDocumentCoordinateUnit.Point, page.CoordinateUnit);
            Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
            var image = Assert.IsType<DataContent>(chat.Requests[i].Messages[0].Contents[1]);
            Assert.Equal("image/png", image.MediaType);
            using var bitmap = SKBitmap.Decode(image.Data.ToArray());
            Assert.Equal((int)page.Dimensions!.Value.Width * 2, bitmap.Width);
            Assert.Equal((int)page.Dimensions.Value.Height * 2, bitmap.Height);
        }

        input.AssertStillReadable((byte)'%');
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public async Task ExtractAsync_ZeroPagePdf_PreservesEmptyResponseArrayWithoutChatOrInventedFrames()
    {
        var chat = new RecordingChatClient();
        using var input = new BorrowedInput(PdfFixture.Create(empty: true));
        using var client = new VisionLlmOcrClient(chat);
        var result = await client.ExtractAsync(input, "empty.pdf", "application/pdf");
        Assert.Equal("empty.pdf", result.Identifier);
        Assert.Empty(Assert.IsType<ChatResponse[]>(result.RawRepresentation));
        Assert.Empty(result.SourcePages);
        Assert.Empty(result.Sections);
        Assert.Empty(chat.Requests);
        client.Dispose();
        input.AssertStillReadable((byte)'%');
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public async Task GetServiceAndDispose_PreserveBorrowedChatAndValidateConstructor()
    {
        Assert.Throws<ArgumentNullException>(() => new VisionLlmOcrClient(null!));
        var chat = new RecordingChatClient(RecordingChatClient.Response("still usable"));
        var client = new VisionLlmOcrClient(chat);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLlmOcrClient).BaseType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLlmOcrClient).GetMethod(nameof(client.ExtractAsync))!.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLlmOcrClient).GetMethod(nameof(client.GetService))!.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLlmOcrClient).GetMethod(nameof(client.Dispose))!.DeclaringType);
        Assert.Same(client, client.GetService(typeof(VisionLlmOcrClient)));
        Assert.Same(client, client.GetService(typeof(DocumentExtractionClient)));
        Assert.Same(client, client.GetService(typeof(object)));
        Assert.Same(chat, client.GetService(typeof(IChatClient)));
        Assert.Null(client.GetService(typeof(string)));
        Assert.Null(client.GetService(typeof(VisionLlmOcrClient), "key"));
        Assert.Equal((typeof(VisionLlmOcrClient), (object?)"key"), chat.ServiceRequests[^1]);
        Assert.Throws<ArgumentNullException>(() => client.GetService(null!));
        client.Dispose();
        client.Dispose();
        using var input = new BorrowedInput([2, 3, 4]);
        var result = await client.ExtractAsync(input, "after-dispose.png", "image/png");
        Assert.Equal("still usable", Assert.IsType<TextContent>(Assert.Single(result.EnumerateContent()).Content).Text);
        input.AssertStillReadable(2);
        Assert.Equal(0, chat.DisposeCalls);
    }
}
