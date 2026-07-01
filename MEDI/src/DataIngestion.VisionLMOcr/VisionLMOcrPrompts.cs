namespace CommunityToolkit.DataIngestion.VisionLMOcr;

internal static class VisionLMOcrPrompts
{
    public const string DefaultSystemPrompt = "You are a precise OCR engine. Extract all visible text from the provided image exactly as it appears. Preserve line breaks and formatting. Output only the extracted text, no commentary.";

    public const string DefaultUserPrompt = "Extract all text from this image.";
}
