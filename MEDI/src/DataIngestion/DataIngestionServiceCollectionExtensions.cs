using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityToolkit.DataIngestion;

/// <summary>
/// Extension methods for registering ingestion pipeline builders with dependency injection.
/// </summary>
public static class DataIngestionServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IngestionPipelineBuilder{T}"/> singleton for composing
    /// ingestion pipelines with LLM-powered chunk processors.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddIngestionPipeline&lt;string&gt;()
    ///     .UseEntityExtraction()
    ///     .UseTopicClassification(o => o.Taxonomy = ["web", "data", "security"])
    ///     .UseHypotheticalQueries(o => o.QuestionsPerChunk = 5)
    ///     .UseTreeIndex();
    /// </code>
    /// </example>
    public static IngestionPipelineBuilder<T> AddIngestionPipeline<T>(
        this IServiceCollection services)
    {
        var pipelineBuilder = new IngestionPipelineBuilder<T>();
        services.AddSingleton(pipelineBuilder);
        return pipelineBuilder;
    }

    /// <summary>
    /// Registers an <see cref="IngestionPipelineBuilder{T}"/> with <c>string</c> content type (the common case).
    /// </summary>
    public static IngestionPipelineBuilder<string> AddIngestionPipeline(
        this IServiceCollection services)
        => services.AddIngestionPipeline<string>();
}
