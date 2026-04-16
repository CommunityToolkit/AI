// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Redis;

/// <summary>
/// Service for storing and retrieving vector records, that uses Redis HashSets as the underlying storage.
/// </summary>
/// <typeparam name="TKey">The data type of the record key. Must be <see cref="string"/>.</typeparam>
/// <typeparam name="TRecord">The data model to use for adding, updating and retrieving data from storage.</typeparam>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class RedisHashSetCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>Metadata about vector store record collection.</summary>
    private readonly VectorStoreCollectionMetadata _collectionMetadata;

    internal static readonly CollectionModelBuildingOptions ModelBuildingOptions = new()
    {
        RequiresAtLeastOneVector = false,
        SupportsMultipleVectors = true
    };

    /// <summary>The default options for vector search.</summary>
    private static readonly VectorSearchOptions<TRecord> s_defaultVectorSearchOptions = new();

    /// <summary>The Redis database to read/write records from.</summary>
    private readonly IDatabase _database;

    /// <summary>The model.</summary>
    private readonly CollectionModel _model;

    /// <summary>An array of the names of all the data properties that are part of the Redis payload as RedisValue objects, i.e. all properties except the key and vector properties.</summary>
    private readonly RedisValue[] _dataStoragePropertyNameRedisValues;

    /// <summary>An array of the names of all the data properties that are part of the Redis payload, i.e. all properties except the key and vector properties, plus the generated score property.</summary>
    private readonly string[] _dataStoragePropertyNamesWithScore;

    /// <summary>The mapper to use when mapping between the consumer data model and the Redis record.</summary>
    private readonly RedisHashSetMapper<TRecord> _mapper;

    /// <summary>whether the collection name should be prefixed to the key names before reading or writing to the Redis store.</summary>
    private readonly bool _prefixCollectionNameToKeyNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisHashSetCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="database">The Redis database to read/write records from.</param>
    /// <param name="name">The name of the collection that this <see cref="RedisHashSetCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <exception cref="ArgumentNullException">Throw when parameters are invalid.</exception>
    // TODO: The provider uses unsafe JSON serialization in many places, #11963
    [RequiresUnreferencedCode("The Weaviate provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Weaviate provider is currently incompatible with NativeAOT.")]
    public RedisHashSetCollection(IDatabase database, string name, RedisHashSetCollectionOptions? options = null)
        : this(
            database,
            name,
            static options => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(RedisHashSetDynamicCollection)))
                : new RedisModelBuilder(ModelBuildingOptions).Build(typeof(TRecord), typeof(TKey), options.Definition, options.EmbeddingGenerator),
            options)
    {
    }

    internal RedisHashSetCollection(IDatabase database, string name, Func<RedisHashSetCollectionOptions, CollectionModel> modelFactory, RedisHashSetCollectionOptions? options)
    {
        // Verify.
        Throw.IfNull(database);
        Throw.IfNullOrWhitespace(name);

        if (typeof(TKey) != typeof(string) && typeof(TKey) != typeof(Guid) && typeof(TKey) != typeof(object))
        {
            throw new NotSupportedException("Only string and Guid keys are supported.");
        }

        options ??= RedisHashSetCollectionOptions.Default;

        // Assign.
        _database = database;
        Name = name;
        _model = modelFactory(options);

        _prefixCollectionNameToKeyNames = options.PrefixCollectionNameToKeyNames;

        // Lookup storage property names.
        _dataStoragePropertyNameRedisValues = _model.DataProperties.Select(p => RedisValue.Unbox(p.StorageName)).ToArray();
        _dataStoragePropertyNamesWithScore = [.. _model.DataProperties.Select(p => p.StorageName), "vector_score"];

        // Assign Mapper.
        _mapper = new RedisHashSetMapper<TRecord>(_model);

        _collectionMetadata = new()
        {
            VectorStoreSystemName = RedisConstants.VectorStoreSystemName,
            VectorStoreName = database.Database.ToString(),
            CollectionName = name
        };
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.FT().InfoAsync(Name).ConfigureAwait(false);
            return true;
        }
        // "Unknown index name" is returned in Redis Stack
        // "no such index" is returned in Redis Alpine
        catch (RedisServerException ex) when (ex.Message.Contains("Unknown index name") || ex.Message.Contains("no such index"))
        {
            return false;
        }
        catch (RedisConnectionException ex)
        {
            throw new VectorStoreException("Call to vector store failed.", ex)
            {
                VectorStoreSystemName = RedisConstants.VectorStoreSystemName,
                VectorStoreName = _collectionMetadata.VectorStoreName,
                CollectionName = Name,
                OperationName = "FT.INFO"
            };
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        const string OperationName = "FT.CREATE";

        // Don't even try to create if the collection already exists.
        if (await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            // Map the record definition to a schema.
            var schema = RedisCollectionCreateMapping.MapToSchema(_model.Properties, useDollarPrefix: false);

            // Create the index creation params.
            // Add the collection name and colon as the index prefix, which means that any record where the key is prefixed with this text will be indexed by this index
            var createParams = new FTCreateParams()
                .AddPrefix($"{Name}:")
                .On(IndexDataType.HASH);

            // Create the index.
            await _database.FT().CreateAsync(Name, createParams, schema).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // Since redis only returns textual error messages, we can check here if the index already exists.
            // If it does, we can ignore the error.
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                if (await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch
            {
            }
#pragma warning restore CA1031 // Do not catch general exception types

            throw new VectorStoreException("Call to vector store failed.", ex)
            {
                VectorStoreSystemName = RedisConstants.VectorStoreSystemName,
                VectorStoreName = _collectionMetadata.VectorStoreName,
                CollectionName = Name,
                OperationName = OperationName
            };
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RunOperationAsync("FT.DROPINDEX",
                () => _database.FT().DropIndexAsync(Name)).ConfigureAwait(false);
        }
        catch (VectorStoreException ex) when (ex.InnerException is RedisServerException)
        {
            // The RedisServerException does not expose any reliable way of checking if the index does not exist.
            // It just sets the message to "Unknown index name".
            // We catch the exception and ignore it, but only after checking that the index does not exist.
            if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        var stringKey = GetStringKey(key);

        // Create Options
        var maybePrefixedKey = PrefixKeyIfNeeded(stringKey);

        var includeVectors = options?.IncludeVectors ?? false;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var operationName = includeVectors ? "HGETALL" : "HMGET";

        // Get the Redis value.
        HashEntry[] retrievedHashEntries;
        if (includeVectors)
        {
            retrievedHashEntries = await RunOperationAsync(
                operationName,
                () => _database.HashGetAllAsync(maybePrefixedKey)).ConfigureAwait(false);
        }
        else
        {
            var fieldKeys = _dataStoragePropertyNameRedisValues;
            var retrievedValues = await RunOperationAsync(
                operationName,
                () => _database.HashGetAsync(maybePrefixedKey, fieldKeys)).ConfigureAwait(false);
            retrievedHashEntries = fieldKeys.Zip(retrievedValues, (field, value) => new HashEntry(field, value)).Where(x => x.Value.HasValue).ToArray();
        }

        // Return null if we found nothing.
        if (retrievedHashEntries == null || retrievedHashEntries.Length == 0)
        {
            return default;
        }

        // Convert to the caller's data model.
        return _mapper.MapFromStorageToDataModel((stringKey, retrievedHashEntries), includeVectors);
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        var stringKey = GetStringKey(key);

        // Create Options
        var maybePrefixedKey = PrefixKeyIfNeeded(stringKey);

        // Remove.
        return RunOperationAsync(
            "DEL",
            () => _database
                .KeyDeleteAsync(maybePrefixedKey));
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        (_, var generatedEmbeddings) = await RedisFieldMapping.ProcessEmbeddingsAsync<TRecord>(_model, [record], cancellationToken).ConfigureAwait(false);

        await UpsertCoreAsync(record, 0, generatedEmbeddings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(records);

        (records, var generatedEmbeddings) = await RedisFieldMapping.ProcessEmbeddingsAsync<TRecord>(_model, records, cancellationToken).ConfigureAwait(false);

        var i = 0;

        foreach (var record in records)
        {
            await UpsertCoreAsync(record, i++, generatedEmbeddings, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpsertCoreAsync(TRecord record, int recordIndex, IReadOnlyList<Embedding>?[]? generatedEmbeddings, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(record);

        // Auto-generate key if needed (client-side for Redis)
        var keyProperty = _model.KeyProperty;
        if (keyProperty.IsAutoGenerated && keyProperty.GetValue<Guid>(record) == Guid.Empty)
        {
            keyProperty.SetValue(record, Guid.NewGuid());
        }

        // Map.
        var redisHashSetRecord = _mapper.MapFromDataToStorageModel(record, recordIndex, generatedEmbeddings);

        // Upsert.
        var maybePrefixedKey = PrefixKeyIfNeeded(redisHashSetRecord.Key);

        await RunOperationAsync(
            "HSET",
            () => _database
                .HashSetAsync(
                    maybePrefixedKey,
                    redisHashSetRecord.HashEntries)).ConfigureAwait(false);
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

        object vector = searchValue switch
        {
            // float32
            ReadOnlyMemory<float> r => r,
            float[] f => new ReadOnlyMemory<float>(f),
            Embedding<float> e => e.Vector,

            // float64
            ReadOnlyMemory<double> r => r,
            double[] f => new ReadOnlyMemory<double>(f),
            Embedding<double> e => e.Vector,

            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false),

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), RedisModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

        // Build query & search.
        var selectFields = options.IncludeVectors ? null : _dataStoragePropertyNamesWithScore;
        byte[] vectorBytes = RedisCollectionSearchMapping.ValidateVectorAndConvertToBytes(vector, "HashSet");
        var query = RedisCollectionSearchMapping.BuildQuery(
            vectorBytes,
            top,
            options,
            _model,
            vectorProperty,
            selectFields);
        var results = await RunOperationAsync(
            "FT.SEARCH",
            () => _database
                .FT()
                .SearchAsync(Name, query)).ConfigureAwait(false);

        // Loop through result and convert to the caller's data model.
        var mappedResults = results.Documents.Select(result =>
        {
            var retrievedHashEntries = _model.DataProperties.Select(p => p.StorageName)
                .Concat(_model.VectorProperties.Select(p => p.StorageName))
                .Select(propertyName => new HashEntry(propertyName, result[propertyName]))
                .ToArray();

            // Convert to the caller's data model.
            var dataModel = _mapper.MapFromStorageToDataModel((RemoveKeyPrefixIfNeeded(result.Id), retrievedHashEntries), options.IncludeVectors);

            // Process the score of the result item.
            var vectorProperty = _model.GetVectorPropertyOrSingle(options);
            var distanceFunction = RedisCollectionSearchMapping.ResolveDistanceFunction(vectorProperty);
            var score = RedisCollectionSearchMapping.GetOutputScoreFromRedisScore(result["vector_score"].HasValue ? (float)result["vector_score"] : null, distanceFunction);

            return new VectorSearchResult<TRecord>(dataModel, score);
        });

        foreach (var result in mappedResults)
        {
            // Apply score threshold filtering. The score semantics depend on the distance function:
            // - For similarity functions (CosineSimilarity, DotProductSimilarity): higher = more similar, filter out below threshold
            // - For distance functions (CosineDistance, EuclideanSquaredDistance): lower = more similar, filter out above threshold
            if (options.ScoreThreshold.HasValue && result.Score.HasValue)
            {
                var distanceFunction = RedisCollectionSearchMapping.ResolveDistanceFunction(vectorProperty);
                var passesThreshold = distanceFunction switch
                {
                    DistanceFunction.CosineSimilarity or DistanceFunction.DotProductSimilarity => result.Score.Value >= options.ScoreThreshold.Value,
                    DistanceFunction.CosineDistance or DistanceFunction.EuclideanSquaredDistance => result.Score.Value <= options.ScoreThreshold.Value,
                    _ => throw new InvalidOperationException($"Unexpected distance function: {distanceFunction}")
                };

                if (!passesThreshold)
                {
                    continue;
                }
            }

            yield return result;
        }
    }

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

        Query query = RedisCollectionSearchMapping.BuildQuery(filter, top, options, _model);

        var results = await RunOperationAsync(
            "FT.SEARCH",
            () => _database
                .FT()
                .SearchAsync(Name, query)).ConfigureAwait(false);

        foreach (var document in results.Documents)
        {
            var retrievedHashEntries = _model.DataProperties.Select(p => p.StorageName)
                .Concat(_model.VectorProperties.Select(p => p.StorageName))
                .Select(propertyName => new HashEntry(propertyName, document[propertyName]))
                .ToArray();

            // Convert to the caller's data model.
            yield return _mapper.MapFromStorageToDataModel((RemoveKeyPrefixIfNeeded(document.Id), retrievedHashEntries), options.IncludeVectors);
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreCollectionMetadata) ? _collectionMetadata :
            serviceType == typeof(IDatabase) ? _database :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    /// <summary>
    /// Prefix the key with the collection name if the option is set.
    /// </summary>
    /// <param name="key">The key to prefix.</param>
    /// <returns>The updated key if updating is required, otherwise the input key.</returns>
    private string PrefixKeyIfNeeded(string key)
    {
        if (_prefixCollectionNameToKeyNames)
        {
            return $"{Name}:{key}";
        }

        return key;
    }

    /// <summary>
    /// Remove the prefix of the given key if the option is set.
    /// </summary>
    /// <param name="key">The key to remove a prefix from.</param>
    /// <returns>The updated key if updating is required, otherwise the input key.</returns>
    private string RemoveKeyPrefixIfNeeded(string key)
    {
        var prefixLength = Name.Length + 1;

        if (_prefixCollectionNameToKeyNames && key.Length > prefixLength)
        {
            return key.Substring(prefixLength);
        }

        return key;
    }

    /// <summary>
    /// Run the given operation and wrap any Redis exceptions with <see cref="VectorStoreException"/>."/>
    /// </summary>
    /// <typeparam name="T">The response type of the operation.</typeparam>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    private Task<T> RunOperationAsync<T>(string operationName, Func<Task<T>> operation)
        => VectorStoreErrorHandler.RunOperationAsync<T, RedisException>(
            _collectionMetadata,
            operationName,
            operation);

    /// <summary>
    /// Run the given operation and wrap any Redis exceptions with <see cref="VectorStoreException"/>."/>
    /// </summary>
    /// <param name="operationName">The type of database operation being run.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The result of the operation.</returns>
    private Task RunOperationAsync(string operationName, Func<Task> operation)
        => VectorStoreErrorHandler.RunOperationAsync<RedisException>(
            _collectionMetadata,
            operationName,
            operation);

    private string GetStringKey(TKey key)
    {
        Throw.IfNull(key);

        var stringKey = key switch
        {
            string s => s,
            Guid g => g.ToString(),

            _ => throw new UnreachableException("string key should have been validated during model building")
        };

        Throw.IfNullOrWhitespace(stringKey);

        return stringKey;
    }
}
