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
    /// Initializes a new instance of the <see cref="MilvusCollectionOptions"/> class.
    /// </summary>
    public MilvusCollectionOptions()
    {
    }

    internal MilvusCollectionOptions(MilvusCollectionOptions? source) : base(source)
    {
    }
}
