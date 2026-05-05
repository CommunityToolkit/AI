using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.DataRetrieval.UnitTests.Utils;

/// <summary>
/// In-memory <see cref="VectorStoreCollection{TKey, TRecord}"/> fake for testing
/// components that depend on vector search.
/// </summary>
public sealed class TestVectorStoreCollection : VectorStoreCollection<string, Dictionary<string, object?>>
{
    private readonly List<VectorSearchResult<Dictionary<string, object?>>> _searchResults = [];

    public override string Name => "test-collection";

    /// <summary>Configures search results to return.</summary>
    public void SetSearchResults(params VectorSearchResult<Dictionary<string, object?>>[] results)
    {
        _searchResults.Clear();
        _searchResults.AddRange(results);
    }

    /// <summary>Adds a search result with the given content and score.</summary>
    public void AddSearchResult(string content, double score, Dictionary<string, object?>? extraProps = null)
    {
        var record = new Dictionary<string, object?>
        {
            ["content"] = content,
            ["score"] = score
        };

        if (extraProps is not null)
        {
            foreach (var kv in extraProps)
                record[kv.Key] = kv.Value;
        }

        _searchResults.Add(new(record, score));
    }

    public override async IAsyncEnumerable<VectorSearchResult<Dictionary<string, object?>>> SearchAsync<TInput>(
        TInput query,
        int top = 5,
        VectorSearchOptions<Dictionary<string, object?>>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var result in _searchResults.Take(top))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return result;
        }
    }

    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task<Dictionary<string, object?>?> GetAsync(
        string key,
        RecordRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Dictionary<string, object?>?>(null);

    public override Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task<string> UpsertAsync(Dictionary<string, object?> record, CancellationToken cancellationToken = default)
        => Task.FromResult("test-key");

    public override Task<IReadOnlyList<string>> UpsertAsync(
        IEnumerable<Dictionary<string, object?>> records,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(["test-key"]);

    public override IAsyncEnumerable<Dictionary<string, object?>> GetAsync(
        Expression<Func<Dictionary<string, object?>, bool>> filter,
        int top = 100,
        FilteredRecordRetrievalOptions<Dictionary<string, object?>>? options = null,
        CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<Dictionary<string, object?>>();

    public override object? GetService(Type serviceType, object? serviceKey = null) => null;
}
