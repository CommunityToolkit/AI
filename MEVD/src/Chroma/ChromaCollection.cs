// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// Service for storing and retrieving vector records, that uses Chroma as the underlying storage.
/// </summary>
/// <typeparam name="TKey">The data type of the record key. Can be either <see cref="string"/> or <see cref="Guid"/>.</typeparam>
/// <typeparam name="TRecord">The data model to use for adding, updating and retrieving data from storage.</typeparam>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class ChromaCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>, IKeywordHybridSearchable<TRecord>
    where TKey : notnull
    where TRecord : class
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>Metadata about vector store record collection.</summary>
    private readonly VectorStoreCollectionMetadata _collectionMetadata;

    /// <summary>The default options for vector search.</summary>
    private static readonly VectorSearchOptions<TRecord> s_defaultVectorSearchOptions = new();

    /// <summary>The default options for hybrid search.</summary>
    private static readonly HybridSearchOptions<TRecord> s_defaultHybridSearchOptions = new();

    /// <summary>The name of the upsert operation for telemetry purposes.</summary>
    private const string UpsertName = "Upsert";

    /// <summary>The name of the Delete operation for telemetry purposes.</summary>
    private const string DeleteName = "Delete";

    /// <summary>Chroma client that can be used to manage the collections and records in a Chroma store.</summary>
    private readonly SharedChromaClient _chromaClient;

    /// <summary>Chroma client of the records of the collection.</summary>
    private readonly ChromaCollectionClient _records;

    /// <summary>The model for this collection.</summary>
    private readonly CollectionModel _model;

    /// <summary>A mapper to use for converting between Chroma records and consumer models.</summary>
    private readonly ChromaMapper<TRecord> _mapper;

    /// <summary>The properties to create a BM25 index for when the collection is created.</summary>
    private readonly List<DataPropertyModel> _bm25Properties;

    /// <summary>Whether the collection was disposed: it releases its share of the client only once.</summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="chromaClient">Chroma client that can be used to manage the collections and records in a Chroma store.</param>
    /// <param name="name">The name of the collection that this <see cref="ChromaCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="chromaClient"/> is disposed when the collection is disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="chromaClient"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown for any misconfigured options.</exception>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate ChromaDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate ChromaDynamicCollection instead")]
    public ChromaCollection(ChromaClient chromaClient, string name, bool ownsClient, ChromaCollectionOptions? options = null)
        : this(() => new SharedChromaClient(chromaClient, ownsClient), name, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="clientFactory">Chroma client factory.</param>
    /// <param name="name">The name of the collection that this <see cref="ChromaCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="clientFactory"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown for any misconfigured options.</exception>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate ChromaDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate ChromaDynamicCollection instead")]
    internal ChromaCollection(Func<SharedChromaClient> clientFactory, string name, ChromaCollectionOptions? options = null)
        : this(
            clientFactory,
            name,
            static options => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(ChromaDynamicCollection)))
                : new ChromaModelBuilder().Build(typeof(TRecord), typeof(TKey), options.Definition, options.EmbeddingGenerator),
            options)
    {
    }

    internal ChromaCollection(Func<SharedChromaClient> clientFactory, string name, Func<ChromaCollectionOptions, CollectionModel> modelFactory, ChromaCollectionOptions? options)
    {
        // Verify.
        Throw.IfNull(clientFactory);
        Throw.IfNullOrWhitespace(name);

        if (typeof(TKey) != typeof(string) && typeof(TKey) != typeof(Guid) && typeof(TKey) != typeof(object))
        {
            throw new NotSupportedException("Only string and Guid keys are supported.");
        }

        options ??= ChromaCollectionOptions.Default;

        // Assign.
        Name = name;
        _model = modelFactory(options);
        _mapper = new ChromaMapper<TRecord>(_model);
        _bm25Properties = ChromaCollectionCreateMapping.GetBm25Properties(_model);

        // The code above can throw, so we need to create the client after the model is built and verified.
        // In case an exception is thrown, we don't need to dispose any resources.
        _chromaClient = clientFactory();

        var records = _chromaClient.Client.GetCollectionClient(name).WithMetadataValues(ChromaMetadataValues.Exact);
        _records = _mapper.DocumentProperty is { } documentProperty ? records.WithDocumentCopyKey(documentProperty.StorageName) : records;

        _collectionMetadata = new()
        {
            VectorStoreSystemName = ChromaConstants.VectorStoreSystemName,
            VectorStoreName = _chromaClient.DatabaseName,
            CollectionName = name
        };
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _chromaClient.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync(
            "CollectionExists",
            () => _chromaClient.Client.CollectionExistsAsync(Name, cancellationToken: cancellationToken));

    /// <inheritdoc />
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        // Chroma indexes every metadata field for filtering, so IsIndexed has no effect.
        var definition = ChromaCollectionCreateMapping.MapCollectionDefinition(Name, _model.VectorProperty, _bm25Properties, _mapper.DocumentProperty);

        return RunOperationAsync(
            "EnsureCollectionExists",
            () => _chromaClient.Client.GetOrCreateCollectionAsync(definition, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync(
            "DeleteCollection",
            // Chroma 1.5 gives the lists of the records of a deleted collection to the records of other collections.
            () => _chromaClient.Client.DeleteCollectionIfExistsAsync(Name, deleteRecordsFirst: true, cancellationToken: cancellationToken));

    /// <inheritdoc />
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(key);

        var records = await GetAsync([key], options, cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault();
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

        var ids = keys.Select(key => ChromaFieldMapping.ToId(key)).ToList();
        if (ids.Count == 0)
        {
            yield break;
        }

        var entries = await RunOperationAsync(
            OperationName,
            () => _records.GetAsync(ids, include: GetInclude(includeVectors), cancellationToken: cancellationToken)).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            yield return _mapper.MapFromStorageToDataModel(entry.Id, entry.Embedding, entry.Metadata, entry.Document, includeVectors);
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

        var ids = keys.Select(key => ChromaFieldMapping.ToId(key)).ToList();
        if (ids.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunOperationAsync(
            DeleteName,
            () => _records.DeleteAsync(ids, cancellationToken: cancellationToken));
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

        GeneratedEmbeddings<Embedding<float>>?[]? generatedEmbeddings = null;

        var vectorProperty = _model.VectorProperty;
        if (!ChromaModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
        {
            // The vector property's type isn't natively supported - we need to generate embeddings.
            Debug.Assert(vectorProperty.EmbeddingGenerator is not null);

            var recordsList = records is IReadOnlyList<TRecord> r ? r : records.ToList();
            if (recordsList.Count == 0)
            {
                return;
            }

            records = recordsList;
            generatedEmbeddings = [(GeneratedEmbeddings<Embedding<float>>)await vectorProperty.GenerateEmbeddingsAsync(records.Select(r => vectorProperty.GetValueAsObject(r)), cancellationToken).ConfigureAwait(false)];
        }

        // Create the Chroma records.
        var keyProperty = _model.KeyProperty;
        var ids = new List<string>();
        var embeddings = new List<ReadOnlyMemory<float>>();
        var metadatas = new List<IReadOnlyDictionary<string, object>?>();
        var documents = new List<string?>();
        var recordIndex = 0;
        foreach (var record in records)
        {
            if (keyProperty.IsAutoGenerated && keyProperty.GetValue<Guid>(record) == Guid.Empty)
            {
                keyProperty.SetValue(record, Guid.NewGuid());
            }

            var storageRecord = _mapper.MapFromDataToStorageModel(record, recordIndex++, generatedEmbeddings);
            ids.Add(storageRecord.Id);
            embeddings.Add(storageRecord.Embedding);
            metadatas.Add(storageRecord.Metadata);
            documents.Add(storageRecord.Document);
        }

        if (ids.Count == 0)
        {
            return;
        }

        var chromaRecords = new ChromaRecords(ids)
        {
            Embeddings = embeddings,
            Metadatas = metadatas,
            Documents = _mapper.HasDocument ? documents : null,
            NullDocumentsDelete = true,
        };

        await RunOperationAsync(
            UpsertName,
            () => _records.UpsertAsync(chromaRecords, cancellationToken)).ConfigureAwait(false);
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

        var filter = options.Filter is not null
            ? new ChromaFilterTranslator().Translate(options.Filter, _model, _mapper.DocumentProperty)
            : ChromaWhereOperator.All;

        var vector = await GetSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        var include = ChromaQueryInclude.Metadatas | ChromaQueryInclude.Distances;
        if (_mapper.HasDocument)
        {
            include |= ChromaQueryInclude.Documents;
        }
        if (options.IncludeVectors)
        {
            include |= ChromaQueryInclude.Embeddings;
        }

        // The scores come from the distances in the space of the vector property.
        var query = new ChromaQuery([vector])
        {
            NResults = top,
            Offset = options.Skip,
            Where = filter,
            Include = include,
            ExpectedSpace = ChromaCollectionCreateMapping.GetSpace(vectorProperty),
        };
        var entries = await RunOperationAsync(
            "Query",
            () => _records.QueryAsync(query, cancellationToken)).ConfigureAwait(false);

        foreach (var entry in entries[0])
        {
            var score = ChromaCollectionSearchMapping.ToScore(entry.Distance!.Value, vectorProperty.DistanceFunction);
            if (!ChromaCollectionSearchMapping.PassesThreshold(score, options.ScoreThreshold, vectorProperty.DistanceFunction))
            {
                continue;
            }

            yield return new VectorSearchResult<TRecord>(
                _mapper.MapFromStorageToDataModel(entry.Id, entry.Embedding, entry.Metadata, entry.Document, options.IncludeVectors),
                score);
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
        if (options.IncludeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var vectorProperty = _model.GetVectorPropertyOrSingle<TRecord>(new() { VectorProperty = options.VectorProperty });
        var textProperty = _model.GetFullTextDataPropertyOrSingle(options.AdditionalProperty);

        var filter = options.Filter is not null
            ? new ChromaFilterTranslator().Translate(options.Filter, _model, _mapper.DocumentProperty)
            : ChromaWhereOperator.All;

        var vector = await GetSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        List<string> select = [ChromaSearchKeys.Metadata, ChromaSearchKeys.Score];
        if (_mapper.HasDocument)
        {
            select.Add(ChromaSearchKeys.Document);
        }
        if (options.IncludeVectors)
        {
            select.Add(ChromaSearchKeys.Embedding);
        }

        var entries = await RunOperationAsync(
            "Search",
            async () =>
            {
                var index = await _records.FindBm25IndexAsync(textProperty.StorageName, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"The Chroma collection '{Name}' has no BM25 index on the text of the property '{textProperty.ModelName}', which hybrid search needs. " +
                        "Only Chroma Cloud has BM25 indexes.");
                return await _records.SearchAsync(
                    new ChromaSearch
                    {
                        Where = filter,
                        Rank = ChromaRank.HybridRrf(vector, string.Join(" ", keywords), index.Key, top + options.Skip),
                        Limit = top,
                        Offset = options.Skip,
                        Select = select,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            var score = (double)entry.Score!.Value;
            if (options.ScoreThreshold is { } threshold && score < threshold)
            {
                continue;
            }

            yield return new VectorSearchResult<TRecord>(
                _mapper.MapFromStorageToDataModel(entry.Id, entry.Embedding, entry.Metadata, entry.Document, options.IncludeVectors),
                score);
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
                ? throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), ChromaModelBuilder.SupportedVectorTypes))
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
            throw new NotSupportedException("Chroma does not support ordering.");
        }

        var chromaFilter = new ChromaFilterTranslator().Translate(filter, _model, _mapper.DocumentProperty);

        var entries = await RunOperationAsync(
            "Get",
            () => _records.GetAsync(
                ids: null,
                chromaFilter,
                whereDocument: null,
                top,
                options.Skip,
                GetInclude(options.IncludeVectors),
                cancellationToken)).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            yield return _mapper.MapFromStorageToDataModel(entry.Id, entry.Embedding, entry.Metadata, entry.Document, options.IncludeVectors);
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreCollectionMetadata) ? _collectionMetadata :
            serviceType == typeof(ChromaClient) ? _chromaClient.Client :
            // Hybrid search needs the BM25 indexes, which the client creates only on Chroma Cloud.
            serviceType == typeof(IKeywordHybridSearchable<TRecord>) ? (_bm25Properties.Count > 0 && _chromaClient.Client.Options.IsChromaCloud ? this : null) :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    private ChromaGetInclude GetInclude(bool includeVectors)
        => ChromaGetInclude.Metadatas
            | (includeVectors ? ChromaGetInclude.Embeddings : 0)
            | (_mapper.HasDocument ? ChromaGetInclude.Documents : 0);

    /// <summary>
    /// Run the given operation and wrap any <see cref="ChromaException"/> with <see cref="VectorStoreException"/>.
    /// </summary>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    private Task RunOperationAsync(string operationName, Func<Task> operation)
        => VectorStoreErrorHandler.RunOperationAsync<ChromaException>(_collectionMetadata, operationName, operation);

    /// <summary>
    /// Run the given operation and wrap any <see cref="ChromaException"/> with <see cref="VectorStoreException"/>.
    /// </summary>
    /// <typeparam name="T">The response type of the operation.</typeparam>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    // Awaited here rather than returned: otherwise the NativeAOT compiler of .NET 10 fails on the state machine of the handler (dotnet/runtime#120847).
    private async Task<T> RunOperationAsync<T>(string operationName, Func<Task<T>> operation)
        => await VectorStoreErrorHandler.RunOperationAsync<T, ChromaException>(_collectionMetadata, operationName, operation).ConfigureAwait(false);
}
