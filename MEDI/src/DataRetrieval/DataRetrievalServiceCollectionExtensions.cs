using Microsoft.Extensions.DataRetrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetrievalPipeline = Microsoft.Extensions.DataRetrieval.RetrievalPipeline;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Extension methods for registering retrieval pipeline builders with dependency injection.
/// </summary>
public static class DataRetrievalServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="RetrievalPipeline"/> singleton and returns a
    /// <see cref="RetrievalPipelineBuilder"/> for composing processors.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddRetrievalPipeline()
    ///     .UseQueryExpansion(o => o.VariantCount = 5)
    ///     .UseLlmReranking()
    ///     .UseCrag();
    /// </code>
    /// </example>
    public static RetrievalPipelineBuilder AddRetrievalPipeline(
        this IServiceCollection services)
    {
        var pipelineBuilder = new RetrievalPipelineBuilder();

        services.AddSingleton(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var pipeline = new RetrievalPipeline(loggerFactory: loggerFactory);

            foreach (var factory in pipelineBuilder.QueryProcessorFactories)
                pipeline.QueryProcessors.Add(factory(sp));

            foreach (var factory in pipelineBuilder.ResultProcessorFactories)
                pipeline.ResultProcessors.Add(factory(sp));

            return pipeline;
        });

        return pipelineBuilder;
    }

    /// <summary>
    /// Registers a <see cref="RetrievalPipeline"/> singleton using a custom factory and returns a
    /// <see cref="RetrievalPipelineBuilder"/> for composing processors on top.
    /// </summary>
    public static RetrievalPipelineBuilder AddRetrievalPipeline(
        this IServiceCollection services,
        Func<IServiceProvider, RetrievalPipeline> pipelineFactory)
    {
        var pipelineBuilder = new RetrievalPipelineBuilder();

        services.AddSingleton(sp =>
        {
            var pipeline = pipelineFactory(sp);

            foreach (var factory in pipelineBuilder.QueryProcessorFactories)
                pipeline.QueryProcessors.Add(factory(sp));

            foreach (var factory in pipelineBuilder.ResultProcessorFactories)
                pipeline.ResultProcessors.Add(factory(sp));

            return pipeline;
        });

        return pipelineBuilder;
    }
}
