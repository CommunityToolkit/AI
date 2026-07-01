using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityToolkit.DataIngestion.VisionLMOcr;

/// <summary>
/// Extension methods for registering <see cref="VisionLMOcrClient"/> with dependency injection.
/// </summary>
public static class VisionLMOcrServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="VisionLMOcrClient"/> as the provider-neutral <see cref="IOcrClient"/> implementation.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <returns>The supplied <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddVisionLMOcrClient(this IServiceCollection services)
        => services.AddVisionLMOcrClient(configure: null);

    /// <summary>
    /// Registers <see cref="VisionLMOcrClient"/> as the provider-neutral <see cref="IOcrClient"/> implementation.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">An optional callback used to configure the OCR prompts and model metadata.</param>
    /// <returns>The supplied <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddVisionLMOcrClient(
        this IServiceCollection services,
        Action<VisionLMOcrOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var options = new VisionLMOcrOptions();
            configure?.Invoke(options);
            return new VisionLMOcrClient(sp.GetRequiredService<IChatClient>(), options);
        });

        services.AddSingleton<IOcrClient>(sp => sp.GetRequiredService<VisionLMOcrClient>());

        return services;
    }
}
