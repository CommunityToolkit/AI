using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataRetrieval;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Fluent builder for composing a <see cref="RetrievalPipeline"/> with query and result processors.
/// Returned by <see cref="DataRetrievalServiceCollectionExtensions.AddRetrievalPipeline(IServiceCollection)"/>.
/// </summary>
public class RetrievalPipelineBuilder
{
    internal List<Func<IServiceProvider, RetrievalQueryProcessor>> QueryProcessorFactories { get; } = [];
    internal List<Func<IServiceProvider, RetrievalResultProcessor>> ResultProcessorFactories { get; } = [];

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
