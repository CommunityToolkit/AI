using System;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring ONNX-based page segmentation services.
/// </summary>
public static class OnnxPageSegmenterServiceCollectionExtensions
{
    /// <summary>
    /// Adds an <see cref="OnnxPageSegmenter"/> and its dependencies to the service collection.
    /// </summary>
    /// <typeparam name="TModel">
    /// The <see cref="ILayoutDetectionModel"/> implementation to use (e.g.
    /// <see cref="RtDetrLayoutModel"/> or <see cref="ConfigurableLayoutModel"/>).
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="OnnxSegmenterOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddOnnxPageSegmenter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TModel>(
        this IServiceCollection services,
        Action<OnnxSegmenterOptions>? configure = null)
        where TModel : class, ILayoutDetectionModel
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<OnnxSegmenterOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<ILayoutDetectionModel, TModel>();
        services.TryAddSingleton<OnnxPageSegmenter>();
        services.TryAddSingleton<IPageSegmenter>(sp => sp.GetRequiredService<OnnxPageSegmenter>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OnnxSegmenterOptions>, OnnxSegmenterOptionsValidator>());

        return services;
    }
}
