// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// Options when creating a <see cref="ChromaCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class ChromaCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly ChromaCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaCollectionOptions"/> class.
    /// </summary>
    public ChromaCollectionOptions()
    {
    }

    internal ChromaCollectionOptions(ChromaCollectionOptions? source) : base(source)
    {
    }
}
