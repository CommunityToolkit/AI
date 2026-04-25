// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.Weaviate;

/// <summary>
/// Options when creating a <see cref="WeaviateCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class WeaviateCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly WeaviateCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaviateCollectionOptions"/> class.
    /// </summary>
    public WeaviateCollectionOptions()
    {
    }

    internal WeaviateCollectionOptions(WeaviateCollectionOptions? source) : base(source)
    {
        Endpoint = source?.Endpoint;
        ApiKey = source?.ApiKey;
        HasNamedVectors = source?.HasNamedVectors ?? Default.HasNamedVectors;
    }

    /// <summary>
    /// Weaviate endpoint for remote or local cluster.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Weaviate API key.
    /// </summary>
    /// <remarks>
    /// This parameter is optional because authentication may be disabled in local clusters for testing purposes.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the vectors in the store are named and multiple vectors are supported, or whether there is just a single unnamed vector in Weaviate collection.
    /// Defaults to multiple named vectors.
    /// <see href="https://weaviate.io/developers/weaviate/config-refs/schema/multi-vector"/>.
    /// </summary>
    public bool HasNamedVectors { get; set; } = true;
}
