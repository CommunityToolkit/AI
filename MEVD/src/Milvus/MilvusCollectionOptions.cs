// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.Milvus;

/// <summary>
/// Options when creating a <see cref="MilvusCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class MilvusCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly MilvusCollectionOptions Default = new();

    /// <summary>
    /// Gets or sets how long <see cref="MilvusCollection{TKey, TRecord}.EnsureCollectionExistsAsync"/> waits for a collection to load.
    /// </summary>
    /// <value>A positive timeout. The default is 11 minutes.</value>
    /// <remarks>
    /// The default exceeds the 600-second server load timeout configured by queryCoord.loadTimeoutSeconds in Milvus 2.6.25.
    /// Increase this value if the server is configured with a longer load timeout.
    /// </remarks>
    public TimeSpan LoadTimeout { get; set; } = TimeSpan.FromMinutes(11);

    /// <summary>
    /// Gets or sets the <c>M</c> parameter of the Hierarchical Navigable Small World (HNSW) algorithm.
    /// </summary>
    /// <value>A positive number of connections per node. The default is 16.</value>
    /// <remarks>
    /// <c>M</c> controls the maximum number of connections per node in the HNSW graph.
    /// Higher values generally improve recall but increase memory usage and index construction time.
    /// Applies to HNSW indexes when creating a collection;
    /// indexes on existing collections are unchanged.
    /// </remarks>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// Gets or sets the <c>efConstruction</c> parameter of the Hierarchical Navigable Small World (HNSW) algorithm.
    /// </summary>
    /// <value>A positive number of candidates. The default is 200.</value>
    /// <remarks>
    /// <c>efConstruction</c> controls the size of the candidate-neighbor list maintained while finding connections
    /// for a new node during index construction. Higher values generally improve index quality but increase build time.
    /// Applies to HNSW indexes when creating a collection;
    /// indexes on existing collections are unchanged.
    /// </remarks>
    public int HnswEfConstruction { get; set; } = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="MilvusCollectionOptions"/> class.
    /// </summary>
    public MilvusCollectionOptions()
    {
    }

    internal MilvusCollectionOptions(MilvusCollectionOptions? source) : base(source)
    {
        LoadTimeout = source?.LoadTimeout ?? LoadTimeout;
        HnswM = source?.HnswM ?? HnswM;
        HnswEfConstruction = source?.HnswEfConstruction ?? HnswEfConstruction;
    }
}
