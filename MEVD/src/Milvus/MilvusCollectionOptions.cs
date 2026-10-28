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
    /// Initializes a new instance of the <see cref="MilvusCollectionOptions"/> class.
    /// </summary>
    public MilvusCollectionOptions()
    {
    }

    internal MilvusCollectionOptions(MilvusCollectionOptions? source) : base(source)
    {
        LoadTimeout = source?.LoadTimeout ?? LoadTimeout;
    }
}
