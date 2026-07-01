using System;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring PdfPig data ingestion services.
/// </summary>
public static class PdfPigDataIngestionServiceCollectionExtensions
{
    /// <summary>
    /// Adds a <see cref="PdfPigOcrReader"/> to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="policy">Controls when the reader invokes OCR. Defaults to native text only.</param>
    /// <param name="ocrClient">Optional OCR client instance to register for the reader.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPdfPigOcrReader(
        this IServiceCollection services,
        OcrPolicy policy = OcrPolicy.Never,
        IOcrClient? ocrClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (ocrClient is not null)
        {
            services.AddSingleton(ocrClient);
        }

        services.TryAddSingleton(sp => new PdfPigOcrReader(
            sp.GetService<IOcrClient>(),
            policy));

        return services;
    }
}
