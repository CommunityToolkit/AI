// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.Milvus;

/// <summary>
/// Options when creating a <see cref="MilvusVectorStore"/>.
/// </summary>
public sealed class MilvusVectorStoreOptions
{
    internal static readonly MilvusVectorStoreOptions Default = new();

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vector embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MilvusVectorStoreOptions"/> class.
    /// </summary>
    public MilvusVectorStoreOptions()
    {
    }

    internal MilvusVectorStoreOptions(MilvusVectorStoreOptions? source)
    {
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }
}
