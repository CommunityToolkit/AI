using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CommunityToolkit.DataIngestion;

/// <summary>
/// Fluent builder for composing <see cref="IngestionPipeline{T}"/> instances with
/// document and chunk processors. Registered via
/// <see cref="DataIngestionServiceCollectionExtensions.AddIngestionPipeline{T}"/>.
/// </summary>
/// <typeparam name="T">The chunk content type (typically <c>string</c>).</typeparam>
public class IngestionPipelineBuilder<T>
{
    internal List<Func<IServiceProvider, IngestionDocumentProcessor>> DocumentProcessorFactories { get; } = [];
    internal List<Func<IServiceProvider, IngestionChunkProcessor<T>>> ChunkProcessorFactories { get; } = [];

    /// <summary>Adds a document processor created by the given factory.</summary>
    public IngestionPipelineBuilder<T> UseDocumentProcessor(
        Func<IServiceProvider, IngestionDocumentProcessor> factory)
    {
        DocumentProcessorFactories.Add(factory);
        return this;
    }

    /// <summary>Adds a document processor resolved from DI.</summary>
    public IngestionPipelineBuilder<T> UseDocumentProcessor<TProcessor>()
        where TProcessor : IngestionDocumentProcessor
    {
        DocumentProcessorFactories.Add(sp =>
            ActivatorUtilities.CreateInstance<TProcessor>(sp));
        return this;
    }

    /// <summary>Adds a chunk processor created by the given factory.</summary>
    public IngestionPipelineBuilder<T> UseChunkProcessor(
        Func<IServiceProvider, IngestionChunkProcessor<T>> factory)
    {
        ChunkProcessorFactories.Add(factory);
        return this;
    }

    /// <summary>Adds a chunk processor resolved from DI.</summary>
    public IngestionPipelineBuilder<T> UseChunkProcessor<TProcessor>()
        where TProcessor : IngestionChunkProcessor<T>
    {
        ChunkProcessorFactories.Add(sp =>
            ActivatorUtilities.CreateInstance<TProcessor>(sp));
        return this;
    }

    /// <summary>Adds entity extraction (people, organizations, technologies, versions) to chunks.</summary>
    public IngestionPipelineBuilder<T> UseEntityExtraction()
    {
        ChunkProcessorFactories.Add(sp =>
            (IngestionChunkProcessor<T>)(object)new EntityExtractionProcessor(
                sp.GetRequiredService<IChatClient>(),
                sp.GetService<ILogger<EntityExtractionProcessor>>()));
        return this;
    }

    /// <summary>Adds topic classification with a configurable taxonomy.</summary>
    public IngestionPipelineBuilder<T> UseTopicClassification(
        Action<TopicClassificationOptions>? configure = null)
    {
        ChunkProcessorFactories.Add(sp =>
        {
            var options = new TopicClassificationOptions();
            configure?.Invoke(options);
            return (IngestionChunkProcessor<T>)(object)new TopicClassificationProcessor(
                sp.GetRequiredService<IChatClient>(),
                options.Taxonomy,
                sp.GetService<ILogger<TopicClassificationProcessor>>());
        });
        return this;
    }

    /// <summary>Adds hypothetical query generation for reverse-HyDE chunk enrichment.</summary>
    public IngestionPipelineBuilder<T> UseHypotheticalQueries(
        Action<HypotheticalQueryOptions>? configure = null)
    {
        ChunkProcessorFactories.Add(sp =>
        {
            var options = new HypotheticalQueryOptions();
            configure?.Invoke(options);
            return (IngestionChunkProcessor<T>)(object)new HypotheticalQueryProcessor(
                sp.GetRequiredService<IChatClient>(),
                options.QuestionsPerChunk,
                sp.GetService<ILogger<HypotheticalQueryProcessor>>());
        });
        return this;
    }

    /// <summary>Adds RAPTOR-style tree index generation (leaf → branch → root summaries).</summary>
    public IngestionPipelineBuilder<T> UseTreeIndex()
    {
        ChunkProcessorFactories.Add(sp =>
            (IngestionChunkProcessor<T>)(object)new TreeIndexProcessor(
                sp.GetRequiredService<IChatClient>(),
                sp.GetService<ILogger<TreeIndexProcessor>>()));
        return this;
    }

    /// <summary>
    /// Creates a configured <see cref="IngestionPipeline{T}"/> with all registered processors.
    /// The caller is responsible for disposing the returned pipeline.
    /// </summary>
    public IngestionPipeline<T> Build(
        IServiceProvider serviceProvider,
        IngestionDocumentReader reader,
        IngestionChunker<T> chunker,
        IngestionChunkWriter<T> writer,
        ILoggerFactory? loggerFactory = null)
    {
        var pipeline = new IngestionPipeline<T>(reader, chunker, writer, loggerFactory: loggerFactory);

        foreach (var factory in DocumentProcessorFactories)
            pipeline.DocumentProcessors.Add(factory(serviceProvider));

        foreach (var factory in ChunkProcessorFactories)
            pipeline.ChunkProcessors.Add(factory(serviceProvider));

        return pipeline;
    }
}

/// <summary>Options for <see cref="TopicClassificationProcessor"/>.</summary>
public class TopicClassificationOptions
{
    /// <summary>Valid topic labels for classification.</summary>
    public string[] Taxonomy { get; set; } = ["web", "data", "performance", "security", "architecture"];
}

/// <summary>Options for <see cref="HypotheticalQueryProcessor"/>.</summary>
public class HypotheticalQueryOptions
{
    /// <summary>Number of questions to generate per chunk.</summary>
    public int QuestionsPerChunk { get; set; } = 3;
}
