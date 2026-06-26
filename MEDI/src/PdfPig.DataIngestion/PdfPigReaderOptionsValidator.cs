using Microsoft.Extensions.Options;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>
/// Validates <see cref="PdfPigReaderOptions"/> at startup.
/// </summary>
internal sealed class PdfPigReaderOptionsValidator : IValidateOptions<PdfPigReaderOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, PdfPigReaderOptions options)
    {
        if (options.RenderDpi <= 0)
        {
            return ValidateOptionsResult.Fail("RenderDpi must be a positive integer.");
        }

        return ValidateOptionsResult.Success;
    }
}
