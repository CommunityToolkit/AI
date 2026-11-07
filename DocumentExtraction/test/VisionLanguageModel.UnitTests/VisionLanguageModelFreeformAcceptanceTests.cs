using CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.VisionLanguageModel.UnitTests;

public sealed class VisionLanguageModelFreeformAcceptanceTests
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
        using var client = new VisionLanguageModelDocumentExtractionClient(chat, new() { Prompt = "Literal transcription, please." });
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
        using var client = new VisionLanguageModelDocumentExtractionClient(chat);
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

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public async Task ExtractAsync_ChatFailure_PropagatesWithoutRetryAndBorrowsResources(string mediaType)
    {
        var failure = new InvalidOperationException("deterministic chat failure");
        var chat = new RecordingChatClient(failure, RecordingChatClient.Response("forbidden fallback"));
        using var input = new BorrowedInput([4, 5, 6]);
        var client = new VisionLanguageModelDocumentExtractionClient(chat);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ExtractAsync(input, "fail", mediaType)));
        Assert.Single(chat.Requests);
        Assert.Null(chat.Requests[0].Options!.ResponseFormat);
        client.Dispose();
        client.Dispose();
        input.AssertStillReadable(4);
        Assert.Equal(0, chat.DisposeCalls);
        Assert.Equal("forbidden fallback", (await chat.GetResponseAsync([])).Text);
    }

    [Theory]
    [InlineData("before-copy", 0, "image/png")]
    [InlineData("during-copy", 0, "image/png")]
    [InlineData("during-chat", 1, "image/png")]
    [InlineData("before-copy", 0, "application/pdf")]
    [InlineData("during-copy", 0, "application/pdf")]
    [InlineData("during-chat", 1, "application/pdf")]
    public async Task ExtractAsync_Cancellation_PreservesTokenAndBorrowedResources(string stage, int calls, string mediaType)
    {
        using var cancellation = new CancellationTokenSource();
        var chat = new RecordingChatClient(RecordingChatClient.Response("must not return"))
        {
            OnRequest = stage == "during-chat" ? _ => cancellation.Cancel() : null,
        };
        using var input = new BorrowedInput([8, 9, 10], stage == "during-copy" ? cancellation : null);
        var client = new VisionLanguageModelDocumentExtractionClient(chat);
        if (stage == "before-copy")
        {
            cancellation.Cancel();
        }

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExtractAsync(input, "cancel", mediaType, cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(calls, chat.Requests.Count);
        if (calls == 1)
        {
            Assert.Equal(cancellation.Token, chat.Requests[0].Token);
            Assert.Equal(mediaType, Assert.IsType<DataContent>(chat.Requests[0].Messages[0].Contents[1]).MediaType);
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
        using var client = new VisionLanguageModelDocumentExtractionClient(chat);
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
    public async Task ExtractAsync_MultipagePdf_ForwardsOriginalBytesInOneCallWithoutInventedPageMetadata()
    {
        var response = RecordingChatClient.Response("# Complete document\n42", new object());
        var chat = new RecordingChatClient(response);
        byte[] bytes = PdfFixture.Create();
        using var input = new BorrowedInput([0, .. bytes]);
        input.Position = 1;
        using var client = new VisionLanguageModelDocumentExtractionClient(chat);
        var result = await client.ExtractAsync(input, "native.pdf", "APPLICATION/PDF", new() { ExtractorName = "pdf-route" });
        var request = Assert.Single(chat.Requests);
        Assert.Equal("pdf-route", request.Options!.ModelId);
        Assert.Null(request.Options.ResponseFormat);
        var message = Assert.Single(request.Messages);
        Assert.Equal(ChatRole.User, message.Role);
        var content = Assert.IsType<DataContent>(message.Contents[1]);
        Assert.Equal(bytes, content.Data.ToArray());
        Assert.Equal("APPLICATION/PDF", content.MediaType);
        Assert.Contains("Transcribe this document", Assert.IsType<TextContent>(message.Contents[0]).Text);
        var page = Assert.Single(result.SourcePages);
        Assert.Null(page.PageNumber);
        Assert.Null(page.Dimensions);
        Assert.Null(page.CoordinateOrigin);
        Assert.Null(page.CoordinateUnit);
        var section = Assert.Single(result.Sections);
        var source = Assert.Single(section.Sources);
        Assert.Same(page, source.Page);
        Assert.Empty(source.Polygon);
        Assert.Same(response.RawRepresentation, source.GetRawRepresentation());
        Assert.Same(response, result.RawRepresentation);
        Assert.Equal("# Complete document\n42", Assert.IsType<TextContent>(Assert.Single(section.Elements).Content).Text);
        client.Dispose();
        Assert.Equal(input.Length, input.Position);
        input.AssertStillReadable(0);
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public async Task ExtractAsync_ZeroPagePdf_IsForwardedWithoutLocalParsingOrPageCountAssumptions()
    {
        var response = RecordingChatClient.Response("");
        var chat = new RecordingChatClient(response);
        byte[] bytes = PdfFixture.Create(empty: true);
        using var input = new BorrowedInput(bytes);
        using var client = new VisionLanguageModelDocumentExtractionClient(chat);
        var result = await client.ExtractAsync(input, "empty.pdf", "application/pdf");
        Assert.Equal("empty.pdf", result.Identifier);
        Assert.Same(response, result.RawRepresentation);
        Assert.Null(Assert.Single(result.SourcePages).PageNumber);
        Assert.Empty(Assert.Single(result.Sections).Elements);
        var content = Assert.IsType<DataContent>(Assert.Single(chat.Requests).Messages[0].Contents[1]);
        Assert.Equal(bytes, content.Data.ToArray());
        Assert.Equal("application/pdf", content.MediaType);
        client.Dispose();
        input.AssertStillReadable((byte)'%');
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public async Task ExtractAsync_PdfUnsupportedByBackend_PropagatesWithoutRasterizationOrRetry()
    {
        var failure = new NotSupportedException("The selected backend does not support PDF file inputs.");
        var chat = new RecordingChatClient(failure, RecordingChatClient.Response("forbidden fallback"));
        byte[] bytes = PdfFixture.Create();
        using var input = new BorrowedInput(bytes);
        using var client = new VisionLanguageModelDocumentExtractionClient(chat);
        Assert.Same(failure, await Assert.ThrowsAsync<NotSupportedException>(
            () => client.ExtractAsync(input, "native.pdf", "application/pdf")));
        var content = Assert.IsType<DataContent>(Assert.Single(chat.Requests).Messages[0].Contents[1]);
        Assert.Equal(bytes, content.Data.ToArray());
        Assert.Equal("application/pdf", content.MediaType);
        client.Dispose();
        input.AssertStillReadable((byte)'%');
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public async Task GetServiceAndDispose_PreserveBorrowedChatAndValidateConstructor()
    {
        Assert.Throws<ArgumentNullException>(() => new VisionLanguageModelDocumentExtractionClient(null!));
        var chat = new RecordingChatClient(RecordingChatClient.Response("still usable"));
        var client = new VisionLanguageModelDocumentExtractionClient(chat);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLanguageModelDocumentExtractionClient).BaseType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLanguageModelDocumentExtractionClient).GetMethod(nameof(client.ExtractAsync))!.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLanguageModelDocumentExtractionClient).GetMethod(nameof(client.GetService))!.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(DocumentExtractionClient), typeof(VisionLanguageModelDocumentExtractionClient).GetMethod(nameof(client.Dispose))!.DeclaringType);
        Assert.Same(client, client.GetService(typeof(VisionLanguageModelDocumentExtractionClient)));
        Assert.Same(client, client.GetService(typeof(DocumentExtractionClient)));
        Assert.Same(client, client.GetService(typeof(object)));
        Assert.Same(chat, client.GetService(typeof(IChatClient)));
        Assert.Null(client.GetService(typeof(string)));
        Assert.Null(client.GetService(typeof(VisionLanguageModelDocumentExtractionClient), "key"));
        Assert.Equal((typeof(VisionLanguageModelDocumentExtractionClient), (object?)"key"), chat.ServiceRequests[^1]);
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
