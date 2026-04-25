// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Grpc.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Qdrant;

/// <summary>
/// Service for storing and retrieving vector records, that uses Qdrant as the underlying storage.
/// </summary>
/// <typeparam name="TKey">The data type of the record key. Can be either <see cref="Guid"/> or <see cref="ulong"/>.</typeparam>
/// <typeparam name="TRecord">The data model to use for adding, updating and retrieving data from storage.</typeparam>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class QdrantCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>, IKeywordHybridSearchable<TRecord>
    where TKey : notnull
    where TRecord : class
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>Metadata about vector store record collection.</summary>
    private readonly VectorStoreCollectionMetadata _collectionMetadata;

    /// <summary>The default options for vector search.</summary>
    private static readonly VectorSearchOptions<TRecord> s_defaultVectorSearchOptions = new();

    /// <summary>The default options for hybrid vector search.</summary>
    private static readonly HybridSearchOptions<TRecord> s_defaultKeywordVectorizedHybridSearchOptions = new();

    /// <summary>The name of the upsert operation for telemetry purposes.</summary>
    private const string UpsertName = "Upsert";

    /// <summary>The name of the Delete operation for telemetry purposes.</summary>
    private const string DeleteName = "Delete";

    /// <summary>Qdrant client that can be used to manage the collections and points in a Qdrant store.</summary>
    private readonly MockableQdrantClient _qdrantClient;

    /// <summary>The model for this collection.</summary>
    private readonly CollectionModel _model;

    /// <summary>A mapper to use for converting between qdrant point and consumer models.</summary>
    private readonly QdrantMapper<TRecord> _mapper;

    /// <summary>Whether the vectors in the store are named and multiple vectors are supported, or whether there is just a single unnamed vector per qdrant point.</summary>
    private readonly bool _hasNamedVectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="qdrantClient">Qdrant client that can be used to manage the collections and points in a Qdrant store.</param>
    /// <param name="name">The name of the collection that this <see cref="QdrantCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="qdrantClient"/> is disposed when the collection is disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="qdrantClient"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown for any misconfigured options.</exception>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate QdrantDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate QdrantDynamicCollection instead")]
    public QdrantCollection(QdrantClient qdrantClient, string name, bool ownsClient, QdrantCollectionOptions? options = null)
        : this(() => new MockableQdrantClient(qdrantClient, ownsClient), name, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="clientFactory">Qdrant client factory.</param>
    /// <param name="name">The name of the collection that this <see cref="QdrantCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="clientFactory"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown for any misconfigured options.</exception>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate QdrantDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate QdrantDynamicCollection instead")]
    internal QdrantCollection(Func<MockableQdrantClient> clientFactory, string name, QdrantCollectionOptions? options = null)
        : this(
            clientFactory,
            name,
            static options => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(QdrantDynamicCollection)))
                : new QdrantModelBuilder(options.HasNamedVectors).Build(typeof(TRecord), typeof(TKey), options.Definition, options.EmbeddingGenerator),
            options)
    {
    }

    internal QdrantCollection(Func<MockableQdrantClient> clientFactory, string name, Func<QdrantCollectionOptions, CollectionModel> modelFactory, QdrantCollectionOptions? options)
    {
        // Verify.
        Throw.IfNull(clientFactory);
        Throw.IfNullOrWhitespace(name);

        if (typeof(TKey) != typeof(ulong) && typeof(TKey) != typeof(Guid) && typeof(TKey) != typeof(object))
        {
            throw new NotSupportedException("Only ulong and Guid keys are supported.");
        }

        options ??= QdrantCollectionOptions.Default;

        // Assign.
        Name = name;
        _model = modelFactory(options);

        _hasNamedVectors = options.HasNamedVectors;
        _mapper = new QdrantMapper<TRecord>(_model, options.HasNamedVectors);

        _collectionMetadata = new()
        {
            VectorStoreSystemName = QdrantConstants.VectorStoreSystemName,
            CollectionName = name
        };

        // The code above can throw, so we need to create the client after the model is built and verified.
        // In case an exception is thrown, we don't need to dispose any resources.
        _qdrantClient = clientFactory();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _qdrantClient.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        return RunOperationAsync(
            "CollectionExists",
            () => _qdrantClient.CollectionExistsAsync(Name, cancellationToken));
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        // Don't even try to create if the collection already exists.
        if (await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (!_hasNamedVectors)
            {
                // If we are not using named vectors, we can only have one vector property. We can assume we have exactly one, since this is already verified in the constructor.
                var singleVectorProperty = _model.VectorProperty;

                // Map the single vector property to the qdrant config.
                var vectorParams = QdrantCollectionCreateMapping.MapSingleVector(singleVectorProperty!);

                // Create the collection with the single unnamed vector.
                await _qdrantClient.CreateCollectionAsync(
                    Name,
                    vectorParams,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Since we are using named vectors, iterate over all vector properties.
                var vectorProperties = _model.VectorProperties;

                // Map the named vectors to the qdrant config.
                var vectorParamsMap = QdrantCollectionCreateMapping.MapNamedVectors(vectorProperties);

                // Create the collection with named vectors.
                await _qdrantClient.CreateCollectionAsync(
                    Name,
                    vectorParamsMap,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // Add indexes for each of the data properties that require filtering.
            var dataProperties = _model.DataProperties.Where(x => x.IsIndexed);
            foreach (var dataProperty in dataProperties)
            {
                // Note that the schema type doesn't distinguish between array and scalar type (so PayloadSchemaType.Integer is used for both integer and array of integers)
                if (QdrantCollectionCreateMapping.s_schemaTypeMap.TryGetValue(dataProperty.Type, out PayloadSchemaType schemaType)
                    || dataProperty.Type.IsArray
                        && QdrantCollectionCreateMapping.s_schemaTypeMap.TryGetValue(dataProperty.Type.GetElementType()!, out schemaType)
                    || dataProperty.Type.IsGenericType
                        && dataProperty.Type.GetGenericTypeDefinition() == typeof(List<>)
                        && QdrantCollectionCreateMapping.s_schemaTypeMap.TryGetValue(dataProperty.Type.GenericTypeArguments[0], out schemaType))
                {
                    await _qdrantClient.CreatePayloadIndexAsync(
                        Name,
                        dataProperty.StorageName,
                        schemaType,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // TODO: This should move to model validation
                    throw new InvalidOperationException($"Property {nameof(VectorStoreDataProperty.IsIndexed)} on {nameof(VectorStoreDataProperty)} '{dataProperty.ModelName}' is set to true, but the property type {dataProperty.Type.Name} is not supported for filtering. The Qdrant VectorStore supports filtering on {string.Join(", ", QdrantCollectionCreateMapping.s_schemaTypeMap.Keys.Select(x => x.Name))} properties only.");
                }
            }

            // Add indexes for each of the data properties that require full text search.
            dataProperties = _model.DataProperties.Where(x => x.IsFullTextIndexed);
            foreach (var dataProperty in dataProperties)
            {
                // TODO: This should move to model validation
                if (dataProperty.Type != typeof(string))
                {
                    throw new InvalidOperationException($"Property {nameof(dataProperty.IsFullTextIndexed)} on {nameof(VectorStoreDataProperty)} '{dataProperty.ModelName}' is set to true, but the property type is not a string. The Qdrant VectorStore supports {nameof(dataProperty.IsFullTextIndexed)} on string properties only.");
                }

                await _qdrantClient.CreatePayloadIndexAsync(
                    Name,
                    dataProperty.StorageName,
                    PayloadSchemaType.Text,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            // Do nothing, since the collection is already created.
        }
        catch (RpcException ex)
        {
            throw new VectorStoreException("Call to vector store failed.", ex)
            {
                VectorStoreSystemName = QdrantConstants.VectorStoreSystemName,
                VectorStoreName = _collectionMetadata.VectorStoreName,
                CollectionName = Name,
                OperationName = "EnsureCollectionExists"
            };
        }
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync("DeleteCollection",
            async () =>
            {
                try
                {
                    await _qdrantClient.DeleteCollectionAsync(Name, null, cancellationToken).ConfigureAwait(false);
                }
                catch (QdrantException)
                {
                    // There is no reliable way to check if the operation failed because the
                    // collection does not exist based on the exception itself.
                    // So we just check here if it exists, and if not, ignore the exception.
                    if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }

                    throw;
                }
            });

    /// <inheritdoc />
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(key);

        var retrievedPoints = await GetAsync([key], options, cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);
        return retrievedPoints.FirstOrDefault();
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string OperationName = "Retrieve";

        Throw.IfNull(keys);

        // Create options.
        var pointsIds = new List<PointId>();

        Type? keyType = null;

        foreach (var key in keys)
        {
            switch (key)
            {
                case ulong id:
                    if (keyType == typeof(Guid))
                    {
                        throw new NotSupportedException("Mixing ulong and Guid keys is not supported");
                    }

                    keyType = typeof(ulong);
                    pointsIds.Add(new PointId { Num = id });
                    break;

                case Guid id:
                    if (keyType == typeof(ulong))
                    {
                        throw new NotSupportedException("Mixing ulong and Guid keys is not supported");
                    }

                    pointsIds.Add(new PointId { Uuid = id.ToString("D") });
                    keyType = typeof(Guid);
                    break;

                default:
                    throw new NotSupportedException($"The provided key type '{key.GetType().Name}' is not supported by Qdrant.");
            }
        }

        var includeVectors = options?.IncludeVectors ?? false;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        // Retrieve data points.
        var retrievedPoints = await RunOperationAsync(
            OperationName,
            () => _qdrantClient.RetrieveAsync(Name, pointsIds, true, includeVectors, cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Convert the retrieved points to the target data model.
        foreach (var retrievedPoint in retrievedPoints)
        {
            yield return _mapper.MapFromStorageToDataModel(retrievedPoint.Id, retrievedPoint.Payload, retrievedPoint.Vectors, includeVectors);
        }
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(key);

        return RunOperationAsync(
            DeleteName,
            () => key switch
            {
                ulong id => _qdrantClient.DeleteAsync(Name, id, wait: true, cancellationToken: cancellationToken),
                Guid id => _qdrantClient.DeleteAsync(Name, id, wait: true, cancellationToken: cancellationToken),
                _ => throw new NotSupportedException($"The provided key type '{key.GetType().Name}' is not supported by Qdrant.")
            });
    }

    /// <inheritdoc />
    public override Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(keys);

        IList? keyList = null;

        switch (keys)
        {
            case IEnumerable<ulong> k:
                keyList = k.ToList();
                break;

            case IEnumerable<Guid> k:
                keyList = k.ToList();
                break;

            case IEnumerable<object> objectKeys:
            {
                // We need to cast the keys to a list of the same type as the first element.
                List<Guid>? guidKeys = null;
                List<ulong>? ulongKeys = null;

                var isFirst = true;
                foreach (var key in objectKeys)
                {
                    if (isFirst)
                    {
                        switch (key)
                        {
                            case ulong l:
                                ulongKeys = [l];
                                keyList = ulongKeys;
                                break;

                            case Guid g:
                                guidKeys = [g];
                                keyList = guidKeys;
                                break;

                            default:
                                throw new NotSupportedException($"The provided key type '{key.GetType().Name}' is not supported by Qdrant.");
                        }

                        isFirst = false;
                        continue;
                    }

                    switch (key)
                    {
                        case ulong u when ulongKeys is not null:
                            ulongKeys.Add(u);
                            continue;

                        case Guid g when guidKeys is not null:
                            guidKeys.Add(g);
                            continue;

                        case Guid or ulong:
                            throw new NotSupportedException("Mixing ulong and Guid keys is not supported");

                        default:
                            throw new NotSupportedException($"The provided key type '{key.GetType().Name}' is not supported by Qdrant.");
                    }
                }

                break;
            }
        }

        if (keyList is { Count: 0 })
        {
            return Task.CompletedTask;
        }

        return RunOperationAsync(
            DeleteName,
            () => keyList switch
            {
                List<ulong> keysList => _qdrantClient.DeleteAsync(
                    Name,
                    keysList,
                    wait: true,
                    cancellationToken: cancellationToken),

                List<Guid> keysList => _qdrantClient.DeleteAsync(
                    Name,
                    keysList,
                    wait: true,
                    cancellationToken: cancellationToken),

                _ => throw new UnreachableException()
            });
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

        // If an embedding generator is defined, invoke it once per property for all records.
        GeneratedEmbeddings<Embedding<float>>?[]? generatedEmbeddings = null;

        var vectorPropertyCount = _model.VectorProperties.Count;
        for (var i = 0; i < vectorPropertyCount; i++)
        {
            var vectorProperty = _model.VectorProperties[i];

            if (QdrantModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
            {
                continue;
            }

            // We have a vector property whose type isn't natively supported - we need to generate embeddings.
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

            // TODO: Ideally we'd group together vector properties using the same generator (and with the same input and output properties),
            // and generate embeddings for them in a single batch. That's some more complexity though.
            generatedEmbeddings ??= new GeneratedEmbeddings<Embedding<float>>?[vectorPropertyCount];
            generatedEmbeddings[i] = (GeneratedEmbeddings<Embedding<float>>)await vectorProperty.GenerateEmbeddingsAsync(records.Select(r => vectorProperty.GetValueAsObject(r)), cancellationToken).ConfigureAwait(false);
        }

        // Create points from records.
        var keyProperty = _model.KeyProperty;
        var pointStructs = new List<PointStruct>();
        var recordIndex = 0;
        foreach (var record in records)
        {
            if (keyProperty.IsAutoGenerated && keyProperty.GetValue<Guid>(record) == Guid.Empty)
            {
                keyProperty.SetValue(record, Guid.NewGuid());
            }

            pointStructs.Add(_mapper.MapFromDataToStorageModel(record, recordIndex++, generatedEmbeddings));
        }

        if (pointStructs.Count == 0)
        {
            return;
        }

        // Upsert.
        await RunOperationAsync(
            UpsertName,
            () => _qdrantClient.UpsertAsync(Name, pointStructs, true, cancellationToken: cancellationToken)).ConfigureAwait(false);
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
        var vectorArray = await GetSearchVectorArrayAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        // Build filter object.
        var filter = options.Filter is not null
            ? new QdrantFilterTranslator().Translate(options.Filter, _model)
            : new Filter();

        // Specify whether to include vectors in the search results.
        var vectorsSelector = new WithVectorsSelector { Enable = options.IncludeVectors };
        var query = new Query { Nearest = new VectorInput(vectorArray) };

        // Execute Search.
        var points = await RunOperationAsync(
            "Query",
            () => _qdrantClient.QueryAsync(
                Name,
                query: query,
                usingVector: _hasNamedVectors ? vectorProperty.StorageName : null,
                filter: filter,
                scoreThreshold: (float?)options.ScoreThreshold,
                limit: (ulong)top,
                offset: (ulong)options.Skip,
                vectorsSelector: vectorsSelector,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Map to data model.
        var mappedResults = points.Select(point => QdrantCollectionSearchMapping.MapScoredPointToVectorSearchResult(
                point,
                _mapper,
                options.IncludeVectors,
                QdrantConstants.VectorStoreSystemName,
                _collectionMetadata.VectorStoreName,
                Name,
                "Query"));

        foreach (var result in mappedResults)
        {
            yield return result;
        }
    }

    private static async ValueTask<float[]> GetSearchVectorArrayAsync<TInput>(TInput searchValue, VectorPropertyModel vectorProperty, CancellationToken cancellationToken)
        where TInput : notnull
    {
        if (searchValue is float[] array)
        {
            return array;
        }

        var memory = searchValue switch
        {
            ReadOnlyMemory<float> r => r,
            Embedding<float> e => e.Vector,
            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => ((Embedding<float>)await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false)).Vector,

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), QdrantModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

        return MemoryMarshal.TryGetArray(memory, out ArraySegment<float> segment) && segment.Count == segment.Array!.Length
                ? segment.Array
                : memory.ToArray();
    }

    #endregion Search

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(filter);
        Throw.IfLessThan(top, 1);

        options ??= new();

        var translatedFilter = new QdrantFilterTranslator().Translate(filter, _model);

        // Specify whether to include vectors in the search results.
        WithVectorsSelector vectorsSelector = new() { Enable = options.IncludeVectors };

        var orderByValues = options.OrderBy?.Invoke(new()).Values;
        var sortInfo = orderByValues switch
        {
            null => null,
            _ when orderByValues.Count == 1 => orderByValues[0],
            _ => throw new NotSupportedException("Qdrant does not support ordering by more than one property.")
        };

        OrderBy? orderBy = null;
        if (sortInfo is not null)
        {
            var orderByName = _model.GetDataOrKeyProperty(sortInfo.PropertySelector).StorageName;
            orderBy = new(orderByName)
            {
                Direction = sortInfo.Ascending ? global::Qdrant.Client.Grpc.Direction.Asc : global::Qdrant.Client.Grpc.Direction.Desc
            };
        }

        var scrollResponse = await RunOperationAsync(
            "Scroll",
            () => _qdrantClient.ScrollAsync(
                Name,
                translatedFilter,
                vectorsSelector,
                limit: (uint)(top + options.Skip),
                orderBy,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var mappedResults = scrollResponse.Result.Skip(options.Skip).Select(point => QdrantCollectionSearchMapping.MapRetrievedPointToRecord(
                point,
                _mapper,
                options.IncludeVectors,
                QdrantConstants.VectorStoreSystemName,
                _collectionMetadata.VectorStoreName,
                Name,
                "Scroll"));

        foreach (var mappedResult in mappedResults)
        {
            yield return mappedResult;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<VectorSearchResult<TRecord>> HybridSearchAsync<TInput>(TInput searchValue, ICollection<string> keywords, int top, HybridSearchOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        Throw.IfLessThan(top, 1);

        // Resolve options.
        options ??= s_defaultKeywordVectorizedHybridSearchOptions;
        var vectorProperty = _model.GetVectorPropertyOrSingle<TRecord>(new() { VectorProperty = options.VectorProperty });
        var vectorArray = await GetSearchVectorArrayAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);
        var textDataProperty = _model.GetFullTextDataPropertyOrSingle(options.AdditionalProperty);

        // Build filter object.
        var filter = options.Filter is not null
            ? new QdrantFilterTranslator().Translate(options.Filter, _model)
            : new Filter();

        // Specify whether to include vectors in the search results.
        var vectorsSelector = new WithVectorsSelector { Enable = options.IncludeVectors };

        // Build the vector query.
        var vectorQuery = new PrefetchQuery
        {
            Filter = filter,
            Query = new Query { Nearest = new VectorInput(vectorArray) }
        };

        if (_hasNamedVectors)
        {
            vectorQuery.Using = _hasNamedVectors ? vectorProperty.StorageName : null;
        }

        // Build the keyword query.
        var keywordFilter = filter.Clone();
        var keywordSubFilter = new Filter();
        foreach (string keyword in keywords)
        {
            keywordSubFilter.Should.Add(new Condition() { Field = new FieldCondition() { Key = textDataProperty.StorageName, Match = new Match { Text = keyword } } });
        }
        keywordFilter.Must.Add(new Condition() { Filter = keywordSubFilter });
        var keywordQuery = new PrefetchQuery
        {
            Filter = keywordFilter,
        };

        // Build the fusion query.
        var fusionQuery = new Query
        {
            Fusion = Fusion.Rrf,
        };

        // Execute Search.
        var points = await RunOperationAsync(
            "Query",
            () => _qdrantClient.QueryAsync(
                Name,
                prefetch: [vectorQuery, keywordQuery],
                query: fusionQuery,
                scoreThreshold: (float?)options.ScoreThreshold,
                limit: (ulong)top,
                offset: (ulong)options.Skip,
                vectorsSelector: vectorsSelector,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Map to data model.
        var mappedResults = points.Select(point => QdrantCollectionSearchMapping.MapScoredPointToVectorSearchResult(
                point,
                _mapper,
                options.IncludeVectors,
                QdrantConstants.VectorStoreSystemName,
                _collectionMetadata.VectorStoreName,
                Name,
                "Query"));

        foreach (var result in mappedResults)
        {
            yield return result;
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreCollectionMetadata) ? _collectionMetadata :
            serviceType == typeof(QdrantClient) ? _qdrantClient.QdrantClient :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    /// <summary>
    /// Run the given operation and wrap any <see cref="RpcException"/> with <see cref="VectorStoreException"/>."/>
    /// </summary>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    private Task RunOperationAsync(string operationName, Func<Task> operation)
        => VectorStoreErrorHandler.RunOperationAsync<RpcException>(
            _collectionMetadata,
            operationName,
            operation);

    /// <summary>
    /// Run the given operation and wrap any <see cref="RpcException"/> with <see cref="VectorStoreException"/>."/>
    /// </summary>
    /// <typeparam name="T">The response type of the operation.</typeparam>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    private Task<T> RunOperationAsync<T>(string operationName, Func<Task<T>> operation)
        => VectorStoreErrorHandler.RunOperationAsync<T, RpcException>(
            _collectionMetadata,
            operationName,
            operation);
}
