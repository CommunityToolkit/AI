// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Milvus.Client.V2;
using Milvus.Client.V2.Requests.Collection;
using Milvus.Client.V2.Requests.Dml;
using Milvus.Client.V2.Requests.Dql;
using Milvus.Client.V2.Responses.Dql;
using Milvus.Client.V2.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Milvus;

/// <summary>
/// Service for storing and retrieving vector records, that uses Milvus as the underlying storage.
/// </summary>
/// <typeparam name="TKey">The data type of the record key. Can be <see cref="long"/>, <see cref="string"/> or <see cref="Guid"/>.</typeparam>
/// <typeparam name="TRecord">The data model to use for adding, updating and retrieving data from storage.</typeparam>
public class MilvusCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>, IKeywordHybridSearchable<TRecord>
    where TKey : notnull
    where TRecord : class
{
    /// <summary>The default options for vector search.</summary>
    private static readonly VectorSearchOptions<TRecord> s_defaultVectorSearchOptions = new();

    private static readonly HybridSearchOptions<TRecord> s_defaultHybridSearchOptions = new();

    /// <summary>Metadata about vector store record collection.</summary>
    private readonly VectorStoreCollectionMetadata _collectionMetadata;

    /// <summary>Milvus client that can be used to manage the collections and records in a Milvus store.</summary>
    private readonly ClientWrapper _clientWrapper;

    /// <summary>The model for this collection.</summary>
    private readonly CollectionModel _model;

    private readonly TimeSpan _loadTimeout;

    private readonly int _hnswM;

    private readonly int _hnswEfConstruction;

    /// <summary>A mapper to use for converting between Milvus records and consumer models.</summary>
    private readonly MilvusMapper<TRecord> _mapper;

    /// <summary>Whether the collection was disposed: it releases its share of the client only once.</summary>
    private int _disposed;

    /// <inheritdoc />
    public override string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MilvusCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="milvusClient">Milvus client that can be used to manage the collections and records in a Milvus store.</param>
    /// <param name="name">The name of the collection that this <see cref="MilvusCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="milvusClient"/> is disposed when the collection is disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="milvusClient"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown for any misconfigured options.</exception>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate MilvusDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate MilvusDynamicCollection instead")]
    public MilvusCollection(MilvusClientV2 milvusClient, string name, bool ownsClient, MilvusCollectionOptions? options = null)
        : this(() => new ClientWrapper(milvusClient, ownsClient), name, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MilvusCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="clientFactory">Milvus client factory.</param>
    /// <param name="name">The name of the collection that this <see cref="MilvusCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="clientFactory"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown for any misconfigured options.</exception>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate MilvusDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate MilvusDynamicCollection instead")]
    internal MilvusCollection(Func<ClientWrapper> clientFactory, string name, MilvusCollectionOptions? options = null)
        : this(
            clientFactory,
            name,
            static options => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(MilvusDynamicCollection)))
                : new MilvusModelBuilder().Build(typeof(TRecord), typeof(TKey), options.Definition, options.EmbeddingGenerator),
            options)
    {
    }

    internal MilvusCollection(Func<ClientWrapper> clientFactory, string name, Func<MilvusCollectionOptions, CollectionModel> modelFactory, MilvusCollectionOptions? options)
    {
        Throw.IfNull(clientFactory);

        // The collection holds the client from here: when the rest of the construction throws, it releases the client,
        // so that a client it was to own is disposed, and a client it shares with its vector store is given back.
        _clientWrapper = clientFactory();
        try
        {
            Throw.IfNullOrWhitespace(name);

            if (typeof(TKey) != typeof(object) && !MilvusModelBuilder.IsKeyTypeSupported(typeof(TKey)))
            {
                throw new NotSupportedException("Only long, string and Guid keys are supported.");
            }

            options ??= MilvusCollectionOptions.Default;

            if (options.LoadTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.LoadTimeout, "LoadTimeout must be positive.");
            }

            if (options.HnswM <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.HnswM, "HnswM must be positive.");
            }

            if (options.HnswEfConstruction <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.HnswEfConstruction, "HnswEfConstruction must be positive.");
            }

            _hnswM = options.HnswM;
            _hnswEfConstruction = options.HnswEfConstruction;
            _loadTimeout = options.LoadTimeout;
            Name = name;
            _model = modelFactory(options);
            _mapper = new MilvusMapper<TRecord>(_model);
        }
        catch
        {
            _clientWrapper.Dispose();
            throw;
        }

        _collectionMetadata = new()
        {
            VectorStoreSystemName = MilvusConstants.VectorStoreSystemName,
            VectorStoreName = _clientWrapper.DatabaseName,
            CollectionName = name
        };
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _clientWrapper.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync("CollectionExists", () => HasCollectionAsync(cancellationToken));

    private async Task<bool> HasCollectionAsync(CancellationToken cancellationToken)
        => (await _clientWrapper.Client.HasCollectionAsync(new HasCollectionReq { CollectionName = Name }, cancellationToken).ConfigureAwait(false)).Has;

    /// <inheritdoc />
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync("EnsureCollectionExists", async () =>
        {
            // The indexes are created with the collection. Those of an existing collection stay as they are: Milvus rejects
            // an index with other settings on an indexed field, or on a field the collection does not have.
            if (!await HasCollectionAsync(cancellationToken).ConfigureAwait(false))
            {
                await _clientWrapper.Client.CreateCollectionAsync(new CreateCollectionReq
                {
                    CollectionName = Name,
                    Schema = MilvusCollectionCreateMapping.MapCollectionSchema(_model),
                    Indexes = MilvusCollectionCreateMapping.MapIndexes(_model, _hnswM, _hnswEfConstruction),
                    ConsistencyLevel = ConsistencyLevel.Strong,
                }, cancellationToken).ConfigureAwait(false);
            }
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        });

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync("DeleteCollection", async () =>
        {
            if (await HasCollectionAsync(cancellationToken).ConfigureAwait(false))
            {
                await _clientWrapper.Client.DropCollectionAsync(new DropCollectionReq { CollectionName = Name }, cancellationToken).ConfigureAwait(false);
            }
        });

    /// <inheritdoc />
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(key);

        return await GetAsync([key], options, cancellationToken).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string OperationName = "Get";

        Throw.IfNull(keys);

        var includeVectors = options?.IncludeVectors ?? false;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            yield break;
        }
        var parameters = CreateQueryParameters(includeVectors);
        var entries = await RunOperationAsync(OperationName,
            () => QueryRowsAsync(KeyFilter(keyList), parameters, cancellationToken)).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            yield return _mapper.MapFromStorageToDataModel(entry, includeVectors);
        }
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(key);

        return DeleteAsync([key], cancellationToken);
    }

    /// <inheritdoc />
    public override Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(keys);

        var keyList = keys.ToList();
        return keyList.Count == 0 ? Task.CompletedTask
            : RunOperationAsync(MilvusConstants.DeleteName, () => _clientWrapper.Client.DeleteAsync(
                new DeleteReq { CollectionName = Name, Expression = KeyFilter(keyList), ConsistencyLevel = ConsistencyLevel.Strong }, cancellationToken));
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(record);

        await UpsertAsync([record], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(records);

        IReadOnlyList<TRecord>? recordsList = null;

        GeneratedEmbeddings<Embedding<float>>?[]? generatedEmbeddings = null;

        var vectorPropertyCount = _model.VectorProperties.Count;
        for (var i = 0; i < vectorPropertyCount; i++)
        {
            var vectorProperty = _model.VectorProperties[i];

            if (MilvusModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
            {
                continue;
            }

            Debug.Assert(vectorProperty.EmbeddingGenerator is not null);

            if (recordsList is null)
            {
                recordsList = records is IReadOnlyList<TRecord> r ? r : records.ToList();

                if (recordsList.Count == 0)
                {
                    return;
                }

                records = recordsList;
            }

            generatedEmbeddings ??= new GeneratedEmbeddings<Embedding<float>>?[vectorPropertyCount];
            generatedEmbeddings[i] = (GeneratedEmbeddings<Embedding<float>>)await vectorProperty.GenerateEmbeddingsAsync(records.Select(r => vectorProperty.GetValueAsObject(r)), cancellationToken).ConfigureAwait(false);
        }

        var keyProperty = _model.KeyProperty;
        var rows = new List<IDictionary<string, object?>>();
        var index = 0;
        foreach (var record in records)
        {
            Throw.IfNull(record);
            if (keyProperty.IsAutoGenerated && keyProperty.GetValue<Guid>(record) == Guid.Empty)
            {
                keyProperty.SetValue(record, Guid.NewGuid());
            }
            rows.Add(_mapper.MapFromDataToStorageModel(record, index++, generatedEmbeddings));
        }
        if (rows.Count > 0)
        {
            await RunOperationAsync(MilvusConstants.UpsertName, () => _clientWrapper.Client.UpsertAsync(new UpsertReq { CollectionName = Name, RowsData = rows }, cancellationToken)).ConfigureAwait(false);
        }
    }

    #region Search

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(searchValue);
        Throw.IfLessThan(top, 1);

        options ??= s_defaultVectorSearchOptions;
        if (options.IncludeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var vectorProperty = _model.GetVectorPropertyOrSingle(options);

        var vector = await GetSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);
        if (vector.Length != vectorProperty.Dimensions)
        {
            throw new ArgumentException($"Search vector must have {vectorProperty.Dimensions} dimensions.", nameof(searchValue));
        }
        var parameters = new SearchParameters
        {
            Offset = options.Skip,
            Expression = options.Filter is null ? null : new MilvusFilterTranslator().Translate(options.Filter, _model),
            ConsistencyLevel = ConsistencyLevel.Strong,
        };
        foreach (var field in GetOutputFields(options.IncludeVectors))
        {
            parameters.OutputFields.Add(field);
        }
        if (MilvusCollectionCreateMapping.GetIndexType(vectorProperty) == IndexType.Hnsw)
        {
            parameters.Ef = Math.Max(MilvusConstants.DefaultHnswSearchEf, top + options.Skip);
        }
        var request = new SearchReq
        {
            CollectionName = Name,
            VectorFieldName = vectorProperty.StorageName,
            Vectors = [vector],
            MetricType = MilvusCollectionCreateMapping.GetMetric(vectorProperty),
            Limit = top,
            Parameters = parameters,
        };
        var results = await RunOperationAsync("Search", () => _clientWrapper.Client.SearchAsync(request, cancellationToken)).ConfigureAwait(false);
        if (IsEmpty(results))
        {
            yield break;
        }
        foreach (var (row, rawScore) in GetHits(results))
        {
            var score = MilvusCollectionSearchMapping.MapScore(rawScore, vectorProperty.DistanceFunction);
            if (MilvusCollectionSearchMapping.PassesThreshold(score, options.ScoreThreshold, vectorProperty.DistanceFunction))
            {
                yield return new VectorSearchResult<TRecord>(_mapper.MapFromStorageToDataModel(row, options.IncludeVectors), score);
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<VectorSearchResult<TRecord>> HybridSearchAsync<TInput>(
        TInput searchValue,
        ICollection<string> keywords,
        int top,
        HybridSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        Throw.IfNull(searchValue);
        Throw.IfNull(keywords);
        Throw.IfLessThan(top, 1);

        options ??= s_defaultHybridSearchOptions;
        if ((long)top + options.Skip > MilvusConstants.MaxSearchLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(top),
                $"Milvus hybrid search ranks at most {MilvusConstants.MaxSearchLimit} records, the skipped ones included: top + Skip is {(long)top + options.Skip}.");
        }
        if (options.IncludeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var vectorProperty = _model.GetVectorPropertyOrSingle<TRecord>(new() { VectorProperty = options.VectorProperty });
        var textProperty = _model.GetFullTextDataPropertyOrSingle(options.AdditionalProperty);
        var filter = options.Filter is null ? null : new MilvusFilterTranslator().Translate(options.Filter, _model);

        var vector = await GetSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);
        if (vector.Length != vectorProperty.Dimensions)
        {
            throw new ArgumentException($"Search vector must have {vectorProperty.Dimensions} dimensions.", nameof(searchValue));
        }

        // Milvus has no offset for hybrid search: the skipped records are asked for too, and dropped.
        var limit = top + options.Skip;
        var candidates = Math.Max(limit, MilvusConstants.HybridSearchCandidates);
        var vectorSearch = new AnnSearchReq
        {
            VectorFieldName = vectorProperty.StorageName,
            Vectors = [vector],
            MetricType = MilvusCollectionCreateMapping.GetMetric(vectorProperty),
            Limit = candidates,
            Expression = filter,
        };
        if (MilvusCollectionCreateMapping.GetIndexType(vectorProperty) == IndexType.Hnsw)
        {
            vectorSearch.ExtraParameters["ef"] = Math.Max(MilvusConstants.DefaultHnswSearchEf, candidates).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var keywordSearch = new AnnSearchReq
        {
            VectorFieldName = MilvusCollectionCreateMapping.GetBm25FieldName(textProperty),
            Texts = [string.Join(" ", keywords)],
            MetricType = MetricType.Bm25,
            Limit = candidates,
            Expression = filter,
        };
        var parameters = new SearchParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        foreach (var field in GetOutputFields(options.IncludeVectors))
        {
            parameters.OutputFields.Add(field);
        }
        var request = new HybridSearchReq
        {
            CollectionName = Name,
            SearchRequests = [vectorSearch, keywordSearch],
            Reranker = new RrfReranker(60),
            Limit = limit,
            Parameters = parameters,
        };
        var results = await RunOperationAsync("HybridSearch", () => _clientWrapper.Client.HybridSearchAsync(request, cancellationToken)).ConfigureAwait(false);
        if (IsEmpty(results))
        {
            yield break;
        }
        foreach (var (row, score) in GetHits(results).Skip(options.Skip))
        {
            // A higher RRF score ranks higher: the threshold is a lower bound.
            if (options.ScoreThreshold is not { } threshold || score >= threshold)
            {
                yield return new VectorSearchResult<TRecord>(_mapper.MapFromStorageToDataModel(row, options.IncludeVectors), score);
            }
        }
    }

    private static bool IsEmpty(SearchResp results) => (results.Ids.LongIds?.Count ?? results.Ids.StringIds?.Count ?? 0) == 0;

    /// <summary>Gets the rows of the hits of a single-vector search, each with its key from the IDs, and their scores.</summary>
    private IEnumerable<(Dictionary<string, object?> Row, float Score)> GetHits(SearchResp results)
    {
        var rows = MilvusFieldMapping.ReadRows(results.FieldsData);
        var keyName = _model.KeyProperty.StorageName;
        for (var i = 0; i < results.Scores.Count; i++)
        {
            var row = i < rows.Count ? rows[i] : new Dictionary<string, object?>(StringComparer.Ordinal);
            row[keyName] = results.Ids.LongIds is { } longIds ? longIds[i] : results.Ids.StringIds![i];
            yield return (row, results.Scores[i]);
        }
    }

    private static async ValueTask<ReadOnlyMemory<float>> GetSearchVectorAsync<TInput>(TInput searchValue, VectorPropertyModel vectorProperty, CancellationToken cancellationToken)
        where TInput : notnull
        => searchValue switch
        {
            float[] array => array,
            ReadOnlyMemory<float> r => r,
            Embedding<float> e => e.Vector,
            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => ((Embedding<float>)await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false)).Vector,

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), MilvusModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

    #endregion Search

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(filter);
        Throw.IfLessThan(top, 1);

        options ??= new();

        if (options.IncludeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        if (options.OrderBy?.Invoke(new()).Values is { Count: > 0 })
        {
            throw new NotSupportedException("Milvus does not support ordering.");
        }

        var milvusFilter = new MilvusFilterTranslator().Translate(filter, _model);
        var parameters = CreateQueryParameters(options.IncludeVectors);
        parameters.Limit = top;
        parameters.Offset = options.Skip;
        var entries = await RunOperationAsync("Get",
            () => QueryRowsAsync(milvusFilter, parameters, cancellationToken)).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            yield return _mapper.MapFromStorageToDataModel(entry, options.IncludeVectors);
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreCollectionMetadata) ? _collectionMetadata :
            serviceType == typeof(MilvusClientV2) ? _clientWrapper.Client :
            serviceType == typeof(IKeywordHybridSearchable<TRecord>) ? (MilvusCollectionCreateMapping.GetFullTextProperties(_model).Any() ? this : null) :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    private string KeyFilter(IEnumerable<TKey> keys)
    {
        var literals = keys.Select(key =>
        {
            Throw.IfNull(key);
            if (key.GetType() != _model.KeyProperty.Type)
            {
                throw new ArgumentException($"Expected key type '{_model.KeyProperty.Type}'.", nameof(keys));
            }
            return MilvusFieldMapping.Literal(key);
        });
        return $"{_model.KeyProperty.StorageName} in [{string.Join(", ", literals)}]";
    }

    private IEnumerable<string> GetOutputFields(bool includeVectors)
        => new[] { _model.KeyProperty.StorageName }.Concat(_model.DataProperties.Select(p => p.StorageName))
            .Concat(includeVectors ? _model.VectorProperties.Select(p => p.StorageName) : []);

    private QueryParameters CreateQueryParameters(bool includeVectors)
    {
        var parameters = new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        foreach (var field in GetOutputFields(includeVectors))
        {
            parameters.OutputFields.Add(field);
        }
        return parameters;
    }

    private async Task<List<Dictionary<string, object?>>> QueryRowsAsync(string filter, QueryParameters parameters, CancellationToken cancellationToken)
    {
        var response = await _clientWrapper.Client.QueryAsync(
            new QueryReq { CollectionName = Name, Expression = filter, Parameters = parameters }, cancellationToken).ConfigureAwait(false);
        return MilvusFieldMapping.ReadRows(response.FieldsData);
    }

    private Task LoadAsync(CancellationToken cancellationToken)
        => _clientWrapper.Client.LoadCollectionAsync(new LoadCollectionReq
        {
            CollectionName = Name,
            Sync = true,
            TimeoutMs = (long)_loadTimeout.TotalMilliseconds,
        }, cancellationToken);

    /// <summary>
    /// Run the given operation and wrap any <see cref="MilvusException"/> with <see cref="VectorStoreException"/>.
    /// </summary>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    private Task RunOperationAsync(string operationName, Func<Task> operation)
        => MilvusErrorHandler.RunAsync(_collectionMetadata, operationName, operation);

    /// <summary>
    /// Run the given operation and wrap any <see cref="MilvusException"/> with <see cref="VectorStoreException"/>.
    /// </summary>
    /// <typeparam name="T">The response type of the operation.</typeparam>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    // Awaited here rather than returned: otherwise the NativeAOT compiler of .NET 10 fails on the state machine of the handler (dotnet/runtime#120847).
    private async Task<T> RunOperationAsync<T>(string operationName, Func<Task<T>> operation)
        => await MilvusErrorHandler.RunAsync<T>(_collectionMetadata, operationName, operation).ConfigureAwait(false);
}
