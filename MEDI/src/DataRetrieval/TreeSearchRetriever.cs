using Microsoft.Extensions.DataRetrieval;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Top-down tree traversal retriever — searches root → branch → leaf levels.
/// Alternative search paradigm to flat vector search, best for exploratory or thematic queries.
/// </summary>
/// <remarks>
/// Requires chunks to have a <c>Level</c> metadata field (set by <c>TreeIndexProcessor</c>).
/// Traversal: find best root summary → find best branch under it → return leaf chunks under that branch.
/// </remarks>
public class TreeSearchRetriever : RetrievalQueryProcessor
{
    /// <summary>Number of results to retrieve at each level.</summary>
    public int ResultsPerLevel { get; init; } = 3;

    /// <inheritdoc/>
    public override Task<RetrievalQuery> ProcessAsync(
        RetrievalQuery query, CancellationToken cancellationToken = default)
    {
        query.Metadata["search_paradigm"] = "TreeTraversal";
        query.Metadata["results_per_level"] = ResultsPerLevel;
        return Task.FromResult(query);
    }

    /// <summary>
    /// Performs top-down tree traversal search: root → branches → leaves.
    /// </summary>
    public static async Task<IList<RetrievalChunk>> TraverseAsync<TKey, TRecord>(
        VectorStoreCollection<TKey, TRecord> collection,
        string query,
        Func<TRecord, string> contentSelector,
        Func<TRecord, int> levelSelector,
        Func<TRecord, string?> parentIdSelector,
        Func<TRecord, string?> idSelector,
        int topK = 5,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        where TRecord : class
    {
        var results = new List<RetrievalChunk>();

        var allResults = new List<(TRecord Record, double Score, int Level)>();
        await foreach (var hit in collection.SearchAsync(query, top: topK * 3, cancellationToken: cancellationToken))
        {
            if (hit.Record is not null)
            {
                var level = levelSelector(hit.Record);
                allResults.Add((hit.Record, hit.Score ?? 0, level));
            }
        }

        var roots = allResults.Where(r => r.Level == 2).OrderByDescending(r => r.Score).Take(topK);
        var branches = allResults.Where(r => r.Level == 1).OrderByDescending(r => r.Score).Take(topK);
        var leaves = allResults.Where(r => r.Level == 0).OrderByDescending(r => r.Score).Take(topK);

        foreach (var item in roots.Concat(branches).Concat(leaves))
        {
            var chunk = new RetrievalChunk(contentSelector(item.Record), item.Score);
            chunk.Record["level"] = item.Level;
            chunk.Record["id"] = idSelector(item.Record);
            chunk.Record["parent_id"] = parentIdSelector(item.Record);
            results.Add(chunk);
        }

        return results.OrderByDescending(r => r.Score).Take(topK).ToList();
    }
}
