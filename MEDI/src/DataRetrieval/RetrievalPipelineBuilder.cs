using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataRetrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Fluent builder for composing a <see cref="RetrievalPipeline"/> with query and result processors.
/// Returned by <see cref="DataRetrievalServiceCollectionExtensions.AddRetrievalPipeline(IServiceCollection)"/>.
/// </summary>
public class RetrievalPipelineBuilder
{
    internal List<Func<IServiceProvider, RetrievalQueryProcessor>> QueryProcessorFactories { get; } = [];
    internal List<Func<IServiceProvider, RetrievalResultProcessor>> ResultProcessorFactories { get; } = [];
    internal IServiceCollection? Services { get; set; }

    /// <summary>Adds a query processor resolved from DI via <see cref="ActivatorUtilities"/>.</summary>
    public RetrievalPipelineBuilder UseQueryProcessor<T>() where T : RetrievalQueryProcessor
    {
        QueryProcessorFactories.Add(sp => ActivatorUtilities.CreateInstance<T>(sp));
        return this;
    }

    /// <summary>Adds adaptive query routing that classifies queries and selects the best search paradigm.</summary>
    public RetrievalPipelineBuilder UseAdaptiveRouting()
    {
        QueryProcessorFactories.Add(sp =>
            new AdaptiveRouter(sp.GetRequiredService<IChatClient>()));
        return this;
    }

    /// <summary>Adds multi-query expansion with Reciprocal Rank Fusion.</summary>
    public RetrievalPipelineBuilder UseQueryExpansion(Action<QueryExpansionOptions>? configure = null)
    {
        QueryProcessorFactories.Add(sp =>
        {
            var options = new QueryExpansionOptions();
            configure?.Invoke(options);
            return new MultiQueryExpander(sp.GetRequiredService<IChatClient>())
            {
                VariantCount = options.VariantCount
            };
        });
        return this;
    }

    /// <summary>Adds HyDE (Hypothetical Document Embeddings) query transformation.</summary>
    public RetrievalPipelineBuilder UseHyDE()
    {
        QueryProcessorFactories.Add(sp =>
            new HydeQueryTransformer(sp.GetRequiredService<IChatClient>()));
        return this;
    }

    /// <summary>Adds RAPTOR-style tree traversal for hierarchical search.</summary>
    public RetrievalPipelineBuilder UseTreeSearch(Action<TreeSearchOptions>? configure = null)
    {
        QueryProcessorFactories.Add(_ =>
        {
            var options = new TreeSearchOptions();
            configure?.Invoke(options);
            return new TreeSearchRetriever
            {
                ResultsPerLevel = options.ResultsPerLevel
            };
        });
        return this;
    }

    /// <summary>Adds a result processor resolved from DI via <see cref="ActivatorUtilities"/>.</summary>
    public RetrievalPipelineBuilder UseResultProcessor<T>() where T : RetrievalResultProcessor
    {
        ResultProcessorFactories.Add(sp => ActivatorUtilities.CreateInstance<T>(sp));
        return this;
    }

    /// <summary>Adds LLM-based reranking of search results.</summary>
    public RetrievalPipelineBuilder UseLlmReranking(Action<LlmRerankingOptions>? configure = null)
    {
        ResultProcessorFactories.Add(sp =>
        {
            var options = new LlmRerankingOptions();
            configure?.Invoke(options);
            return new LlmReranker(sp.GetRequiredService<IChatClient>())
            {
                MaxResults = options.MaxResults,
                MaxCandidates = options.MaxCandidates,
                PreviewLength = options.PreviewLength
            };
        });
        return this;
    }

    /// <summary>Adds CRAG quality gate that routes results based on relevance confidence.</summary>
    public RetrievalPipelineBuilder UseCrag(Action<CragOptions>? configure = null)
    {
        ResultProcessorFactories.Add(sp =>
        {
            var options = new CragOptions();
            configure?.Invoke(options);
            return new CragValidator(sp.GetRequiredService<IChatClient>())
            {
                EvaluateTopN = options.EvaluateTopN,
                PreviewLength = options.PreviewLength
            };
        });
        return this;
    }

    /// <summary>
    /// Registers an <see cref="IRetriever"/> singleton that binds the configured pipeline
    /// to a specific vector store collection. This is a terminal method — call it after
    /// composing all processors.
    /// </summary>
    /// <typeparam name="TKey">The vector store key type.</typeparam>
    /// <typeparam name="TRecord">The vector store record type.</typeparam>
    /// <param name="collectionFactory">Factory to resolve the vector store collection from DI.</param>
    /// <param name="contentSelector">Optional function to extract text content from a record.</param>
    /// <example>
    /// <code>
    /// builder.Services.AddRetrievalPipeline()
    ///     .UseLlmReranking()
    ///     .AsRetriever&lt;string, Article&gt;(
    ///         sp => sp.GetRequiredService&lt;VectorStoreCollection&lt;string, Article&gt;&gt;(),
    ///         record => record.Content);
    /// </code>
    /// </example>
    public void AsRetriever<TKey, TRecord>(
        Func<IServiceProvider, VectorStoreCollection<TKey, TRecord>> collectionFactory,
        Func<TRecord, string>? contentSelector = null)
        where TKey : notnull
        where TRecord : class
    {
        if (Services is null)
        {
            throw new InvalidOperationException(
                "AsRetriever can only be called on a builder returned by AddRetrievalPipeline.");
        }

        Services.AddSingleton<IRetriever>(sp =>
        {
            var pipeline = sp.GetRequiredService<RetrievalPipeline>();
            var collection = collectionFactory(sp);
            return pipeline.AsRetriever(collection, contentSelector);
        });
    }
}

/// <summary>Options for multi-query expansion.</summary>
public class QueryExpansionOptions
{
    /// <summary>Number of alternative query variants to generate.</summary>
    public int VariantCount { get; set; } = 3;
}

/// <summary>Options for tree search retrieval.</summary>
public class TreeSearchOptions
{
    /// <summary>Number of results per tree level.</summary>
    public int ResultsPerLevel { get; set; } = 3;
}

/// <summary>Options for LLM-based reranking.</summary>
public class LlmRerankingOptions
{
    /// <summary>Maximum results to return.</summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>Maximum candidates to evaluate.</summary>
    public int MaxCandidates { get; set; } = 8;

    /// <summary>Maximum preview length per passage.</summary>
    public int PreviewLength { get; set; } = 200;
}

/// <summary>Options for CRAG validation.</summary>
public class CragOptions
{
    /// <summary>Number of top chunks to evaluate.</summary>
    public int EvaluateTopN { get; set; } = 3;

    /// <summary>Maximum preview length per passage.</summary>
    public int PreviewLength { get; set; } = 300;
}
