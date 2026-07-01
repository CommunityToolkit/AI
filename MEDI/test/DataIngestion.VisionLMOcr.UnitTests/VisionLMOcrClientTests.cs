using CommunityToolkit.DataIngestion.VisionLMOcr.UnitTests.Utils;
using Microsoft.Extensions.AI;

namespace CommunityToolkit.DataIngestion.VisionLMOcr.UnitTests;

public class VisionLMOcrClientTests
{
    [Fact]
    public async Task GetTextAsync_ImageInput_ReturnsSingleOcrPageWithVisionSource()
    {
        var client = new TestChatClient("Hello OCR");
        var ocrClient = new VisionLMOcrClient(client);

        var result = await ocrClient.GetTextAsync(CreateImageStream(), "image/png");

        var page = Assert.Single(result.Pages);
        Assert.Equal(0, page.Index);
        Assert.Equal("Hello OCR", page.Markdown);
        Assert.Empty(page.Tables);
        Assert.Equal("Hello OCR", result.Markdown);
        Assert.Equal("vision_lm", result.OcrSource);
    }

    [Fact]
    public async Task GetTextAsync_SendsImageDataContentAndDefaultOcrSystemPrompt()
    {
        var client = new TestChatClient("text");
        var ocrClient = new VisionLMOcrClient(client);

        await ocrClient.GetTextAsync(CreateImageStream(), "image/jpeg");

        var systemMessage = Assert.Single(client.LastMessages.Where(m => m.Role == ChatRole.System));
        Assert.Equal(VisionLMOcrPrompts.DefaultSystemPrompt, systemMessage.Text);

        var userMessage = Assert.Single(client.LastMessages.Where(m => m.Role == ChatRole.User));
        Assert.Contains(userMessage.Contents, c => c is DataContent dc && dc.MediaType == "image/jpeg");
        Assert.Contains(userMessage.Contents, c => c is TextContent tc && tc.Text == VisionLMOcrPrompts.DefaultUserPrompt);
    }

    [Fact]
    public async Task GetTextAsync_ThreadsCustomPromptsAndModelId()
    {
        var client = new TestChatClient("custom text");
        var ocrClient = new VisionLMOcrClient(client, new VisionLMOcrOptions
        {
            SystemPrompt = "custom system",
            UserPrompt = "custom user",
            ModelId = "vision-model",
        });

        var result = await ocrClient.GetTextAsync(CreateImageStream(), "image/png");

        Assert.Equal("vision-model", result.ModelId);
        Assert.Equal("custom system", Assert.Single(client.LastMessages.Where(m => m.Role == ChatRole.System)).Text);

        var userMessage = Assert.Single(client.LastMessages.Where(m => m.Role == ChatRole.User));
        Assert.Contains(userMessage.Contents, c => c is TextContent tc && tc.Text == "custom user");
    }

    [Fact]
    public async Task GetTextAsync_RequestModelIdOverridesDefaultModelId()
    {
        var client = new TestChatClient("custom text");
        var ocrClient = new VisionLMOcrClient(client, new VisionLMOcrOptions { ModelId = "default-model" });

        var result = await ocrClient.GetTextAsync(
            CreateImageStream(),
            "image/png",
            new OcrOptions { ModelId = "request-model" });

        Assert.Equal("request-model", result.ModelId);
    }

    [Fact]
    public async Task GetTextAsync_ReportsProgressAtLeastOnce()
    {
        var client = new TestChatClient("text");
        var ocrClient = new VisionLMOcrClient(client);
        var progress = new RecordingProgress();

        await ocrClient.GetTextAsync(CreateImageStream(), "image/png", progress: progress);

        Assert.NotEmpty(progress.Reports);
        Assert.Contains(progress.Reports, r => r.PagesProcessed == 1 && r.TotalPages == 1);
    }

    [Fact]
    public void GetService_ReturnsSelfForOcrClientAndDelegatesUnknownServices()
    {
        var inner = new TestChatClient("text");
        var client = new VisionLMOcrClient(inner);
        var delegated = new object();
        inner.UnknownService = delegated;
        var serviceKey = new object();

        Assert.Same(client, client.GetService(typeof(IOcrClient)));
        Assert.Same(client, client.GetService(typeof(VisionLMOcrClient)));
        Assert.Same(delegated, client.GetService(typeof(string), serviceKey));
        Assert.Equal(typeof(string), inner.LastServiceType);
        Assert.Same(serviceKey, inner.LastServiceKey);
    }

    private static MemoryStream CreateImageStream()
        => new([0x89, 0x50, 0x4E, 0x47]);

    private sealed class RecordingProgress : IProgress<OcrProgress>
    {
        public List<OcrProgress> Reports { get; } = [];

        public void Report(OcrProgress value) => Reports.Add(value);
    }
}
