using Microsoft.Extensions.Options;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// Validates <see cref="OnnxSegmenterOptions"/> at startup to catch configuration errors early.
/// </summary>
internal sealed class OnnxSegmenterOptionsValidator : IValidateOptions<OnnxSegmenterOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OnnxSegmenterOptions options)
    {
        if (options.ConfidenceThreshold is < 0f or > 1f)
        {
            return ValidateOptionsResult.Fail("ConfidenceThreshold must be between 0.0 and 1.0.");
        }

        if (options.RenderDpi <= 0)
        {
            return ValidateOptionsResult.Fail("RenderDpi must be a positive integer.");
        }

        return ValidateOptionsResult.Success;
    }
}
