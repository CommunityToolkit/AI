// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.DocumentExtraction.Vision.UnitTests;

public sealed class VisionClientOptionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Constructor_NullOrDefaultOptions_UsesFreeformDefaultAndBorrowsClient(bool nullOptions)
    {
        var settings = new VisionDocumentExtractionClientOptions();
        Assert.Null(settings.Prompt);
        Assert.False(settings.UseStructuredOutput);
        var chat = new RecordingChatClient(RecordingChatClient.Response("literal"));
        using var client = new VisionDocumentExtractionClient(chat, nullOptions ? null : settings);
        using var input = new MemoryStream([1, 2, 3]);
        var document = await client.ExtractAsync(input, "image", "image/png");
        var request = Assert.Single(chat.Requests);
        Assert.Contains("GitHub-flavored Markdown", Assert.IsType<TextContent>(request.Messages[0].Contents[0]).Text);
        Assert.Null(request.Options!.ResponseFormat);
        Assert.Equal("literal", Assert.IsType<TextContent>(Assert.Single(document.EnumerateContent()).Content).Text);
        client.Dispose();
        Assert.Equal(0, chat.DisposeCalls);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "custom instruction")]
    [InlineData(true, "custom instruction")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, " \t")]
    [InlineData(true, " \t")]
    public async Task Constructor_SnapshotsPromptAndMode_AndPreservesRequestModelOverride(bool structured, string? prompt)
    {
        var settings = new VisionDocumentExtractionClientOptions { Prompt = prompt, UseStructuredOutput = structured };
        var response = RecordingChatClient.Response(structured ? """{"pages":[{"markdown":"literal"}]}""" : "literal");
        var chat = new RecordingChatClient(response);
        using var client = new VisionDocumentExtractionClient(chat, settings);
        settings.Prompt = "mutated";
        settings.UseStructuredOutput = !structured;
        using var input = new MemoryStream([1, 2, 3]);
        var document = await client.ExtractAsync(input, "image", "image/png", new() { ExtractorName = "request-model" });
        var request = Assert.Single(chat.Requests);
        string instruction = Assert.IsType<TextContent>(request.Messages[0].Contents[0]).Text;
        if (prompt is null)
        {
            Assert.Contains(structured ? "JSON object" : "GitHub-flavored Markdown", instruction);
        }
        else
        {
            Assert.Equal(prompt, instruction);
        }

        Assert.Equal(structured, request.Options!.ResponseFormat is not null);
        Assert.Equal("request-model", request.Options.ModelId);
        Assert.Same(response, document.RawRepresentation);
        Assert.Equal("actual-vision-model", response.ModelId);
        Assert.Equal("literal", Assert.IsType<TextContent>(Assert.Single(document.EnumerateContent()).Content).Text);
        client.Dispose();
        Assert.Equal(0, chat.DisposeCalls);
    }

    [Fact]
    public void Constructor_OnlyExposesDependencyAndToolkitOptions()
    {
        var constructor = Assert.Single(typeof(VisionDocumentExtractionClient).GetConstructors());
        Assert.Equal([typeof(IChatClient), typeof(VisionDocumentExtractionClientOptions)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Throws<ArgumentNullException>(() => new VisionDocumentExtractionClient(null!, new()));
    }
}
