// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using NRedisStack.RedisStackCommands;
using StackExchange.Redis;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Redis;

/// <summary>
/// Class for accessing the list of collections in a Redis vector store.
/// </summary>
/// <remarks>
/// This class can be used with collections of any schema type, but requires you to provide schema information when getting a collection.
/// </remarks>
public sealed class RedisVectorStore : VectorStore
{
    /// <summary>Metadata about vector store.</summary>
    private readonly VectorStoreMetadata _metadata;

    /// <summary>The redis database to read/write indices from.</summary>
    private readonly IDatabase _database;

    /// <summary>A general purpose definition that can be used to construct a collection when needing to proxy schema agnostic operations.</summary>
    private static readonly VectorStoreCollectionDefinition s_generalPurposeDefinition = new() { Properties = [new VectorStoreKeyProperty("Key", typeof(string))] };

    /// <summary>The way in which data should be stored in redis..</summary>
    private readonly RedisStorageType? _storageType;

    private readonly IEmbeddingGenerator? _embeddingGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisVectorStore"/> class.
    /// </summary>
    /// <param name="database">The redis database to read/write indices from.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    [RequiresUnreferencedCode("The Redis provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Redis provider is currently incompatible with NativeAOT.")]
    public RedisVectorStore(IDatabase database, RedisVectorStoreOptions? options = default)
    {
        Throw.IfNull(database);

        _database = database;

        options ??= RedisVectorStoreOptions.Default;
        _storageType = options.StorageType;
        _embeddingGenerator = options.EmbeddingGenerator;

        _metadata = new()
        {
            VectorStoreSystemName = RedisConstants.VectorStoreSystemName,
            VectorStoreName = database.Database.ToString()
        };
    }

    /// <inheritdoc />
    // TODO: The provider uses unsafe JSON serialization in many places, #11963
    [RequiresUnreferencedCode("The Weaviate provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Weaviate provider is currently incompatible with NativeAOT.")]
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
    {
        if (typeof(TRecord) == typeof(Dictionary<string, object?>))
        {
            throw new ArgumentException(VectorDataStrings.GetCollectionWithDictionaryNotSupported);
        }

        return _storageType switch
        {
            RedisStorageType.HashSet => new RedisHashSetCollection<TKey, TRecord>(_database, name, new RedisHashSetCollectionOptions()
            {
                Definition = definition,
                EmbeddingGenerator = _embeddingGenerator
            }),

            RedisStorageType.Json => new RedisJsonCollection<TKey, TRecord>(_database, name, new RedisJsonCollectionOptions()
            {
                Definition = definition,
                EmbeddingGenerator = _embeddingGenerator
            }),

            _ => throw new UnreachableException()
        };
    }

    /// <inheritdoc />
    // TODO: The provider uses unsafe JSON serialization in many places, #11963
    [RequiresUnreferencedCode("The Redis provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Redis provider is currently incompatible with NativeAOT.")]
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
        => _storageType switch
        {
            RedisStorageType.HashSet => new RedisHashSetDynamicCollection(_database, name, new RedisHashSetCollectionOptions()
            {
                Definition = definition,
                EmbeddingGenerator = _embeddingGenerator
            }),

            RedisStorageType.Json => new RedisJsonDynamicCollection(_database, name, new RedisJsonCollectionOptions()
            {
                Definition = definition,
                EmbeddingGenerator = _embeddingGenerator
            }),

            _ => throw new UnreachableException()
        };

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string OperationName = "FT._LIST";

        var listResult = await VectorStoreErrorHandler.RunOperationAsync<RedisResult[], RedisException>(
            _metadata,
            OperationName,
            () => _database.FT()._ListAsync()).ConfigureAwait(false);

        foreach (var item in listResult)
        {
            var name = item.ToString();
            if (name != null)
            {
                yield return name;
            }
        }
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var collection = GetDynamicCollection(name, s_generalPurposeDefinition);
        return collection.CollectionExistsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        var collection = GetDynamicCollection(name, s_generalPurposeDefinition);
        return collection.EnsureCollectionDeletedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreMetadata) ? _metadata :
            serviceType == typeof(IDatabase) ? _metadata :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }
}
