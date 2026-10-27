// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Milvus.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Milvus;

/// <summary>
/// Class for accessing the list of collections in a Milvus vector store.
/// </summary>
/// <remarks>
/// This class can be used with collections of any schema type, but requires you to provide schema information when getting a collection.
/// </remarks>
public sealed class MilvusVectorStore : VectorStore
{
    /// <summary>A general purpose definition that can be used to construct a collection when needing to proxy schema agnostic operations.</summary>
    private static readonly VectorStoreCollectionDefinition s_generalPurposeDefinition = new() { Properties = [new VectorStoreKeyProperty("Key", typeof(string)), new VectorStoreVectorProperty("Vector", typeof(ReadOnlyMemory<float>), 1)] };

    /// <summary>Metadata about vector store.</summary>
    private readonly VectorStoreMetadata _metadata;

    /// <summary>Milvus client that can be used to manage the collections and records in a Milvus store.</summary>
    private readonly SharedMilvusClient _milvusClient;

    private readonly IEmbeddingGenerator? _embeddingGenerator;

    /// <summary>Whether the store was disposed: it releases its share of the client only once.</summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MilvusVectorStore"/> class.
    /// </summary>
    /// <param name="milvusClient">Milvus client that can be used to manage the collections and records in a Milvus store.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="milvusClient"/> is disposed after the vector store and the collections it returns are all disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    public MilvusVectorStore(MilvusClient milvusClient, bool ownsClient, MilvusVectorStoreOptions? options = default)
        : this(new SharedMilvusClient(milvusClient, ownsClient), options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MilvusVectorStore"/> class.
    /// </summary>
    /// <param name="milvusClient">Milvus client that can be used to manage the collections and records in a Milvus store.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    internal MilvusVectorStore(SharedMilvusClient milvusClient, MilvusVectorStoreOptions? options = default)
    {
        Throw.IfNull(milvusClient);

        _milvusClient = milvusClient;

        options ??= MilvusVectorStoreOptions.Default;
        _embeddingGenerator = options.EmbeddingGenerator;

        _metadata = new()
        {
            VectorStoreSystemName = MilvusConstants.VectorStoreSystemName,
            VectorStoreName = milvusClient.DatabaseName
        };
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _milvusClient.Dispose();
        }

        base.Dispose(disposing);
    }

#pragma warning disable IDE0090 // Use 'new(...)'
    /// <inheritdoc />
    [RequiresDynamicCode("This overload of GetCollection() is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    [RequiresUnreferencedCode("This overload of GetCollection() is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
#if NET
    public override MilvusCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#else
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#endif
        => typeof(TRecord) == typeof(Dictionary<string, object?>)
            ? throw new ArgumentException(VectorDataStrings.GetCollectionWithDictionaryNotSupported)
            : new MilvusCollection<TKey, TRecord>(_milvusClient.Share, name, new()
            {
                Definition = definition,
                EmbeddingGenerator = _embeddingGenerator
            });

    /// <inheritdoc />
#if NET
    public override MilvusDynamicCollection GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#else
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#endif
        => new MilvusDynamicCollection(_milvusClient.Share, name, new MilvusCollectionOptions()
        {
            Definition = definition,
            EmbeddingGenerator = _embeddingGenerator
        });
#pragma warning restore IDE0090

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string OperationName = "ListCollectionNames";

        var collections = await MilvusErrorHandler.RunAsync<IReadOnlyList<MilvusCollectionInfo>>(
            new VectorStoreCollectionMetadata { VectorStoreSystemName = MilvusConstants.VectorStoreSystemName, VectorStoreName = _milvusClient.DatabaseName },
            OperationName,
            () => _milvusClient.Client.ListCollectionsAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);

        foreach (var collection in collections)
        {
            yield return collection.Name;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        using var collection = GetDynamicCollection(name, s_generalPurposeDefinition);
        return await collection.CollectionExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        using var collection = GetDynamicCollection(name, s_generalPurposeDefinition);
        await collection.EnsureCollectionDeletedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreMetadata) ? _metadata :
            serviceType == typeof(MilvusClient) ? _milvusClient.Client :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }
}
