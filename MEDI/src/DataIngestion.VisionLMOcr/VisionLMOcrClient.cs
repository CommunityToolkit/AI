using Microsoft.Extensions.AI;

namespace CommunityToolkit.DataIngestion.VisionLMOcr;

/// <summary>
/// Implements <see cref="IOcrClient"/> by sending image bytes to a vision-capable <see cref="IChatClient"/>.
/// </summary>
public sealed class VisionLMOcrClient : IOcrClient
{
    private readonly IChatClient chatClient;
    private readonly VisionLMOcrOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisionLMOcrClient"/> class.
    /// </summary>
    /// <param name="chatClient">The vision-capable chat client used for OCR.</param>
    /// <param name="options">The default OCR options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public VisionLMOcrClient(IChatClient chatClient, VisionLMOcrOptions? options = null)
    {
        this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        this.options = options ?? new VisionLMOcrOptions();
    }

    /// <inheritdoc />
    public async Task<OcrResult> GetTextAsync(
        Stream document,
        string mediaType,
        OcrOptions? options = null,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var imageBytes = buffer.ToArray();

        var systemPrompt = this.options.SystemPrompt ?? VisionLMOcrPrompts.DefaultSystemPrompt;
        var userPrompt = this.options.UserPrompt ?? VisionLMOcrPrompts.DefaultUserPrompt;

        ChatMessage[] messages =
        [
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, (IList<AIContent>)
            [
                new DataContent(imageBytes, mediaType),
                new TextContent(userPrompt),
            ]),
        ];

        var response = await chatClient.GetResponseAsync(
            messages,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = response.Text ?? string.Empty;
        progress?.Report(new OcrProgress
        {
            PagesProcessed = 1,
            TotalPages = 1,
            Status = "Completed",
        });

        return new OcrResult([new OcrPage(0, text)])
        {
            OcrSource = "vision_lm",
            ModelId = options?.ModelId ?? this.options.ModelId,
            RawRepresentation = response,
            Usage = new OcrUsage { PagesProcessed = 1 },
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(IOcrClient) || serviceType == typeof(VisionLMOcrClient))
        {
            return this;
        }

        return chatClient.GetService(serviceType, serviceKey);
    }
}
