// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Pinecone;
using Microsoft.Shared.Diagnostics;
using VectorDataCollectionModel = Microsoft.Extensions.VectorData.ProviderServices.CollectionModel;

namespace CommunityToolkit.VectorData.Pinecone;

/// <summary>
/// Service for storing and retrieving vector records, that uses Pinecone as the underlying storage.
/// </summary>
/// <typeparam name="TKey">The data type of the record key. Must be <see cref="string"/>.</typeparam>
/// <typeparam name="TRecord">The data model to use for adding, updating and retrieving data from storage.</typeparam>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class PineconeCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    private static readonly VectorSearchOptions<TRecord> s_defaultVectorSearchOptions = new();

    /// <summary>Metadata about vector store record collection.</summary>
    private readonly VectorStoreCollectionMetadata _collectionMetadata;

    private readonly PineconeClient _pineconeClient;
    private readonly VectorDataCollectionModel _model;
    private readonly PineconeMapper<TRecord> _mapper;
    private IndexClient? _indexClient;

    /// <inheritdoc />
    public override string Name { get; }

    /// <summary>The namespace within the Pinecone index that will be used for operations involving records (Get, Upsert, Delete).</summary>
    private readonly string? _indexNamespace;

    /// <summary>The public cloud where the serverless index is hosted.</summary>
    private readonly string _serverlessIndexCloud;

    /// <summary>The region where the serverless index is created.</summary>
    private readonly string _serverlessIndexRegion;

    /// <summary>
    /// Initializes a new instance of the <see cref="PineconeCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="pineconeClient">Pinecone client that can be used to manage the collections and vectors in a Pinecone store.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="pineconeClient"/> is null.</exception>
    /// <param name="name">The name of the collection that this <see cref="PineconeCollection{TKey, TRecord}"/> will access.</param>
    /// <exception cref="ArgumentException">Thrown for any misconfigured options.</exception>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate PineconeDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate PineconeDynamicCollection instead.")]
    public PineconeCollection(PineconeClient pineconeClient, string name, PineconeCollectionOptions? options = null)
        : this(
            pineconeClient,
            name,
            static options => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(PineconeDynamicCollection)))
                : new PineconeModelBuilder().Build(typeof(TRecord), typeof(TKey), options.Definition, options.EmbeddingGenerator),
            options)
    {
    }

    internal PineconeCollection(PineconeClient pineconeClient, string name, Func<PineconeCollectionOptions, VectorDataCollectionModel> modelFactory, PineconeCollectionOptions? options)
    {
        Throw.IfNull(pineconeClient);
        VerifyCollectionName(name);

        if (typeof(TKey) != typeof(string) && typeof(TKey) != typeof(Guid) && typeof(TKey) != typeof(object))
        {
            throw new NotSupportedException("Only string and Guid keys are supported.");
        }

        options ??= PineconeCollectionOptions.Default;

        _pineconeClient = pineconeClient;
        Name = name;
        _model = modelFactory(options);

        _indexNamespace = options.IndexNamespace;
        _serverlessIndexCloud = options.ServerlessIndexCloud;
        _serverlessIndexRegion = options.ServerlessIndexRegion;

        _mapper = new PineconeMapper<TRecord>(_model);

        _collectionMetadata = new()
        {
            VectorStoreSystemName = PineconeConstants.VectorStoreSystemName,
            CollectionName = name
        };
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => RunCollectionOperationAsync(
            "CollectionExists",
            async () =>
            {
                var collections = await _pineconeClient.ListIndexesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

                return collections.Indexes?.Any(x => x.Name == Name) is true;
            });

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        // Don't even try to create if the collection already exists.
        if (await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // we already run through record property validation, so a single VectorStoreRecordVectorProperty is guaranteed.
        var vectorProperty = _model.VectorProperty!;

        if (!string.IsNullOrEmpty(vectorProperty.IndexKind) && vectorProperty.IndexKind != "PGA")
        {
            throw new NotSupportedException(
                $"IndexKind of '{vectorProperty.IndexKind}' for property '{vectorProperty.ModelName}' is not supported. Pinecone only supports 'PGA' (Pinecone Graph Algorithm), which is always enabled.");
        }

        CreateIndexRequest request = new()
        {
            Name = Name,
            Dimension = vectorProperty.Dimensions,
            Metric = MapDistanceFunction(vectorProperty),
            Spec = new ServerlessIndexSpec
            {
                Serverless = new ServerlessSpec
                {
                    Cloud = MapCloud(_serverlessIndexCloud),
                    Region = _serverlessIndexRegion,
                }
            },
        };

        try
        {
            await _pineconeClient.CreateIndexAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ConflictError)
        {
            // Do nothing, since the index is already created.
        }
        catch (PineconeApiException other)
        {
            throw new VectorStoreException("Call to vector store failed.", other)
            {
                VectorStoreSystemName = PineconeConstants.VectorStoreSystemName,
                VectorStoreName = _collectionMetadata.VectorStoreName,
                CollectionName = Name,
                OperationName = "EnsureCollectionExists"
            };
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _pineconeClient.DeleteIndexAsync(Name, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (NotFoundError)
        {
            // If the collection does not exist, we should ignore the exception.
        }
        catch (PineconeApiException other)
        {
            throw new VectorStoreException("Call to vector store failed.", other)
            {
                VectorStoreSystemName = PineconeConstants.VectorStoreSystemName,
                VectorStoreName = _collectionMetadata.VectorStoreName,
                CollectionName = Name,
                OperationName = "DeleteCollection"
            };
        }
    }

    /// <inheritdoc />
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        var includeVectors = options?.IncludeVectors is true;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        FetchRequest request = new()
        {
            Namespace = _indexNamespace,
            Ids = [GetStringKey(key)]
        };

        var response = await RunIndexOperationAsync(
            "Get",
            indexClient => indexClient.FetchAsync(request, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = response.Vectors?.Values.FirstOrDefault();
        if (result is null)
        {
            return default;
        }

        return _mapper.MapFromStorageToDataModel(result, includeVectors);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(keys);

        var includeVectors = options?.IncludeVectors is true;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

#pragma warning disable CA1851 // Bogus: Possible multiple enumerations of 'IEnumerable' collection
        var keysList = keys switch
        {
            IEnumerable<string> k => k.ToList(),
            IEnumerable<Guid> k => k.Select(x => x.ToString()).ToList(),
            IEnumerable<object> k => k.Select(x => x.ToString()!).ToList(),
            _ => throw new UnreachableException("string key should have been validated during model building")
        };
#pragma warning restore CA1851

        if (keysList.Count == 0)
        {
            yield break;
        }

        FetchRequest request = new()
        {
            Namespace = _indexNamespace,
            Ids = keysList
        };

        var response = await RunIndexOperationAsync(
            "GetBatch",
            indexClient => indexClient.FetchAsync(request, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (response.Vectors is null || response.Vectors.Count == 0)
        {
            yield break;
        }

        var records = response.Vectors.Values.Select(x => _mapper.MapFromStorageToDataModel(x, includeVectors));

        foreach (var record in records)
        {
            yield return record;
        }
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        DeleteRequest request = new()
        {
            Namespace = _indexNamespace,
            Ids = [GetStringKey(key)]
        };

        return RunIndexOperationAsync(
            "Delete",
            indexClient => indexClient.DeleteAsync(request, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public override Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(keys);

        var keysList = keys switch
        {
            IEnumerable<string> k => k.ToList(),
            IEnumerable<Guid> k => k.Select(x => x.ToString()).ToList(),
            IEnumerable<object> k => k.Select(x => x.ToString()!).ToList(),
            _ => throw new UnreachableException("string key should have been validated during model building")
        };

        if (keysList.Count == 0)
        {
            return Task.CompletedTask;
        }

        DeleteRequest request = new()
        {
            Namespace = _indexNamespace,
            Ids = keysList
        };

        return RunIndexOperationAsync(
            "DeleteBatch",
            indexClient => indexClient.DeleteAsync(request, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(record);

        // Handle auto-generated keys (client-side for Pinecone, which doesn't support server-side auto-generation)
        var keyProperty = _model.KeyProperty;
        if (keyProperty.IsAutoGenerated && keyProperty.GetValue<Guid>(record) == Guid.Empty)
        {
            keyProperty.SetValue(record, Guid.NewGuid());
        }

        // If an embedding generator is defined, invoke it once for all records.
        Embedding<float>? generatedEmbedding = null;

        Debug.Assert(_model.VectorProperties.Count <= 1);
        if (_model.VectorProperties is [var vectorProperty] && !PineconeModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
        {
            // We have a vector property whose type isn't natively supported - we need to generate embeddings.
            Debug.Assert(vectorProperty.EmbeddingGenerator is not null);

            generatedEmbedding = (Embedding<float>)await vectorProperty.GenerateEmbeddingAsync(vectorProperty.GetValueAsObject(record), cancellationToken).ConfigureAwait(false);
        }

        var vector = _mapper.MapFromDataToStorageModel(record, generatedEmbedding);

        UpsertRequest request = new()
        {
            Namespace = _indexNamespace,
            Vectors = [vector],
        };

        await RunIndexOperationAsync(
            "Upsert",
            indexClient => indexClient.UpsertAsync(request, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(records);

        // If an embedding generator is defined, invoke it once for all records.
        GeneratedEmbeddings<Embedding<float>>? generatedEmbeddings = null;

        if (_model.VectorProperties is [var vectorProperty] && !PineconeModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
        {
            // We have a vector property whose type isn't natively supported - we need to generate embeddings.
            Debug.Assert(vectorProperty.EmbeddingGenerator is not null);

            var recordsList = records is IReadOnlyList<TRecord> r ? r : records.ToList();

            if (recordsList.Count == 0)
            {
                return;
            }

            records = recordsList;

            generatedEmbeddings = (GeneratedEmbeddings<Embedding<float>>)await vectorProperty.GenerateEmbeddingsAsync(records.Select(r => vectorProperty.GetValueAsObject(r)), cancellationToken).ConfigureAwait(false);

            Debug.Assert(generatedEmbeddings.Count == recordsList.Count);
        }

        // Handle auto-generated keys (client-side for Pinecone, which doesn't support server-side auto-generation)
        var keyProperty = _model.KeyProperty;
        var vectors = new List<Vector>();
        var recordIndex = 0;
        foreach (var record in records)
        {
            if (keyProperty.IsAutoGenerated && keyProperty.GetValue<Guid>(record) == Guid.Empty)
            {
                keyProperty.SetValue(record, Guid.NewGuid());
            }

            vectors.Add(_mapper.MapFromDataToStorageModel(record, generatedEmbeddings?[recordIndex++]));
        }

        if (vectors.Count == 0)
        {
            return;
        }

        UpsertRequest request = new()
        {
            Namespace = _indexNamespace,
            Vectors = vectors,
        };

        await RunIndexOperationAsync(
            "UpsertBatch",
            indexClient => indexClient.UpsertAsync(request, cancellationToken: cancellationToken)).ConfigureAwait(false);
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

        ReadOnlyMemory<float> vector = searchValue switch
        {
            ReadOnlyMemory<float> r => r,
            float[] f => new ReadOnlyMemory<float>(f),
            Embedding<float> e => e.Vector,
            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => ((Embedding<float>)await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false)).Vector,

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), PineconeModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

        var filter = options.Filter is not null
            ? new PineconeFilterTranslator().Translate(options.Filter, _model)
            : null;

        QueryRequest request = new()
        {
            TopK = (uint)(top + options.Skip),
            Namespace = _indexNamespace,
            IncludeValues = options.IncludeVectors,
            IncludeMetadata = true,
            Vector = vector,
            Filter = filter,
        };

        QueryResponse response = await RunIndexOperationAsync(
            "VectorizedSearch",
            indexClient => indexClient.QueryAsync(request, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (response.Matches is null)
        {
            yield break;
        }

        // Pinecone does not provide a way to skip results, so we need to do it manually.
        var skippedResults = response.Matches
            .Skip(options.Skip);

        var records = skippedResults.Select(
            x => new VectorSearchResult<TRecord>(
                _mapper.MapFromStorageToDataModel(
                    new Vector()
                    {
                        Id = x.Id,
                        Values = x.Values ?? Array.Empty<float>(),
                        Metadata = x.Metadata,
                        SparseValues = x.SparseValues
                    },
                    options.IncludeVectors),
                x.Score));

        foreach (var record in records)
        {
            // Pinecone returns similarity scores where higher values indicate more similar results.
            if (options.ScoreThreshold.HasValue && record.Score < options.ScoreThreshold.Value)
            {
                continue;
            }

            yield return record;
        }
    }

    #endregion Search

    /// <inheritdoc/>
    public override async IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(filter);
        Throw.IfLessThan(top, 1);

        if (options?.OrderBy is not null)
        {
            throw new NotSupportedException("Pinecone does not support ordering.");
        }

        options ??= new();

        if (options.IncludeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        QueryRequest request = new()
        {
            TopK = (uint)(top + options.Skip),
            Namespace = _indexNamespace,
            IncludeValues = options.IncludeVectors,
            IncludeMetadata = true,
            // "Either 'vector' or 'ID' must be provided"
            // Since we are doing a query, we don't have a vector to provide, so we fake one.
            // When https://github.com/pinecone-io/pinecone-dotnet-client/issues/43 gets implemented, we need to switch.
            Vector = new ReadOnlyMemory<float>(new float[_model.VectorProperty.Dimensions]),
            Filter = new PineconeFilterTranslator().Translate(filter, _model),
        };

        QueryResponse response = await RunIndexOperationAsync(
            "Get",
            indexClient => indexClient.QueryAsync(request, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (response.Matches is null)
        {
            yield break;
        }

        var records = response
            .Matches
            .Skip(options.Skip)
            .Select(
                x => _mapper.MapFromStorageToDataModel(
                    new Vector()
                    {
                        Id = x.Id,
                        Values = x.Values ?? Array.Empty<float>(),
                        Metadata = x.Metadata,
                        SparseValues = x.SparseValues
                    },
                    options.IncludeVectors));

        foreach (var record in records)
        {
            yield return record;
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreCollectionMetadata) ? _collectionMetadata :
            serviceType == typeof(PineconeClient) ? _pineconeClient :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    private Task<T> RunIndexOperationAsync<T>(string operationName, Func<IndexClient, Task<T>> operation)
        => VectorStoreErrorHandler.RunOperationAsync<T, PineconeApiException>(
            _collectionMetadata,
            operationName,
            async () =>
            {
                if (_indexClient is null)
                {
                    // If we don't provide "host" to the Index method, it's going to perform
                    // a blocking call to DescribeIndexAsync!!
                    string hostName = (await _pineconeClient.DescribeIndexAsync(Name).ConfigureAwait(false)).Host;
                    _indexClient = _pineconeClient.Index(name: Name, host: hostName);
                }

                return await operation.Invoke(_indexClient).ConfigureAwait(false);
            });

    private Task<T> RunCollectionOperationAsync<T>(string operationName, Func<Task<T>> operation)
        => VectorStoreErrorHandler.RunOperationAsync<T, PineconeApiException>(
            _collectionMetadata,
            operationName,
            operation);

    private static ServerlessSpecCloud MapCloud(string serverlessIndexCloud)
        => serverlessIndexCloud switch
        {
            "aws" => ServerlessSpecCloud.Aws,
            "azure" => ServerlessSpecCloud.Azure,
            "gcp" => ServerlessSpecCloud.Gcp,
            _ => throw new ArgumentException($"Invalid serverless index cloud: {serverlessIndexCloud}.", nameof(serverlessIndexCloud))
        };

    private static CreateIndexRequestMetric MapDistanceFunction(VectorPropertyModel vectorProperty)
        => vectorProperty.DistanceFunction switch
        {
            DistanceFunction.CosineSimilarity or null => CreateIndexRequestMetric.Cosine,
            DistanceFunction.DotProductSimilarity => CreateIndexRequestMetric.Dotproduct,
            DistanceFunction.EuclideanSquaredDistance => CreateIndexRequestMetric.Euclidean,
            _ => throw new NotSupportedException($"Distance function '{vectorProperty.DistanceFunction}' is not supported.")
        };

    private static void VerifyCollectionName(string collectionName)
    {
        Throw.IfNullOrWhitespace(collectionName);

        // Based on https://docs.pinecone.io/troubleshooting/restrictions-on-index-names
        foreach (char character in collectionName)
        {
            if (character is not (>= 'a' and <= 'z' or '-' or >= '0' and <= '9'))
            {
                throw new ArgumentException("Collection name must contain only ASCII lowercase letters, digits and dashes.", nameof(collectionName));
            }
        }
    }

    private string GetStringKey(TKey key)
    {
        Throw.IfNull(key);

        var stringKey = key switch
        {
            string s => s,
            Guid g => g.ToString(),

            _ => throw new UnreachableException()
        };

        Throw.IfNullOrWhitespace(stringKey);

        return stringKey;
    }
}
