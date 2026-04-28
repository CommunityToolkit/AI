using System;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring PdfPig data ingestion services.
/// </summary>
public static class PdfPigDataIngestionServiceCollectionExtensions
{
    /// <summary>
    /// Adds a <see cref="PdfPigReader"/> to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure reader options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPdfPigReader(
        this IServiceCollection services,
        Action<PdfPigReaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PdfPigReaderOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<PdfPigReader>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PdfPigReaderOptions>, PdfPigReaderOptionsValidator>());

        return services;
    }
}
