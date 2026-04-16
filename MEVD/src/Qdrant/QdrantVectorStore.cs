// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Qdrant.Client;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Qdrant;

/// <summary>
/// Class for accessing the list of collections in a Qdrant vector store.
/// </summary>
/// <remarks>
/// This class can be used with collections of any schema type, but requires you to provide schema information when getting a collection.
/// </remarks>
public sealed class QdrantVectorStore : VectorStore
{
    /// <summary>Metadata about vector store.</summary>
    private readonly VectorStoreMetadata _metadata;

    /// <summary>Qdrant client that can be used to manage the collections and points in a Qdrant store.</summary>
    private readonly MockableQdrantClient _qdrantClient;

    /// <summary>A general purpose definition that can be used to construct a collection when needing to proxy schema agnostic operations.</summary>
    private static readonly VectorStoreCollectionDefinition s_generalPurposeDefinition = new() { Properties = [new VectorStoreKeyProperty("Key", typeof(ulong)), new VectorStoreVectorProperty("Vector", typeof(ReadOnlyMemory<float>), 1)] };

    /// <summary>Whether the vectors in the store are named and multiple vectors are supported, or whether there is just a single unnamed vector per qdrant point.</summary>
    private readonly bool _hasNamedVectors;

    private readonly IEmbeddingGenerator? _embeddingGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantVectorStore"/> class.
    /// </summary>
    /// <param name="qdrantClient">Qdrant client that can be used to manage the collections and points in a Qdrant store.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="qdrantClient"/> is disposed after the vector store is disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    public QdrantVectorStore(QdrantClient qdrantClient, bool ownsClient, QdrantVectorStoreOptions? options = default)
        : this(new MockableQdrantClient(qdrantClient, ownsClient), options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantVectorStore"/> class.
    /// </summary>
    /// <param name="qdrantClient">Qdrant client that can be used to manage the collections and points in a Qdrant store.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    internal QdrantVectorStore(MockableQdrantClient qdrantClient, QdrantVectorStoreOptions? options = default)
    {
        Throw.IfNull(qdrantClient);

        _qdrantClient = qdrantClient;

        options ??= QdrantVectorStoreOptions.Default;
        _hasNamedVectors = options.HasNamedVectors;
        _embeddingGenerator = options.EmbeddingGenerator;

        _metadata = new()
        {
            VectorStoreSystemName = QdrantConstants.VectorStoreSystemName
        };
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _qdrantClient.Dispose();
        base.Dispose(disposing);
    }

#pragma warning disable IDE0090 // Use 'new(...)'
    /// <inheritdoc />
    [RequiresDynamicCode("This overload of GetCollection() is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    [RequiresUnreferencedCode("This overload of GetCollecttion() is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
#if NET
    public override QdrantCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#else
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#endif
        => typeof(TRecord) == typeof(Dictionary<string, object?>)
            ? throw new ArgumentException(VectorDataStrings.GetCollectionWithDictionaryNotSupported)
            : new QdrantCollection<TKey, TRecord>(_qdrantClient.Share, name, new()
            {
                HasNamedVectors = _hasNamedVectors,
                Definition = definition,
                EmbeddingGenerator = _embeddingGenerator
            });

    /// <inheritdoc />
#if NET
    public override QdrantDynamicCollection GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#else
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#endif
        => new QdrantDynamicCollection(_qdrantClient.Share, name, new QdrantCollectionOptions()
        {
            HasNamedVectors = _hasNamedVectors,
            Definition = definition,
            EmbeddingGenerator = _embeddingGenerator
        });
#pragma warning restore IDE0090

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var collections = await VectorStoreErrorHandler.RunOperationAsync<IReadOnlyList<string>, RpcException>(
            _metadata,
            "ListCollections",
            () => _qdrantClient.ListCollectionsAsync(cancellationToken)).ConfigureAwait(false);

        foreach (var collection in collections)
        {
            yield return collection;
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
            serviceType == typeof(QdrantClient) ? _qdrantClient.QdrantClient :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }
}
