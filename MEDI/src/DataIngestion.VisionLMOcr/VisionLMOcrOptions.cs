namespace CommunityToolkit.DataIngestion.VisionLMOcr;

/// <summary>
/// Options for configuring <see cref="VisionLMOcrClient"/> prompts and model metadata.
/// </summary>
public sealed class VisionLMOcrOptions
{
    /// <summary>
    /// Gets or sets the model identifier reported on OCR results.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Gets or sets the system prompt sent to the vision-capable chat client.
    /// </summary>
    public string? SystemPrompt { get; set; } = VisionLMOcrPrompts.DefaultSystemPrompt;

    /// <summary>
    /// Gets or sets the user prompt sent with the image content.
    /// </summary>
    public string? UserPrompt { get; set; } = VisionLMOcrPrompts.DefaultUserPrompt;
}
