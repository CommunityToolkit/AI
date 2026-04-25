// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.Qdrant;

/// <summary>
/// Options when creating a <see cref="QdrantVectorStore"/>.
/// </summary>
public sealed class QdrantVectorStoreOptions
{
    internal static readonly QdrantVectorStoreOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantVectorStoreOptions"/> class.
    /// </summary>
    public QdrantVectorStoreOptions()
    {
    }

    internal QdrantVectorStoreOptions(QdrantVectorStoreOptions? source)
    {
        HasNamedVectors = source?.HasNamedVectors ?? Default.HasNamedVectors;
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the vectors in the store are named and multiple vectors are supported, or whether there is just a single unnamed vector per qdrant point.
    /// Defaults to single vector per point.
    /// </summary>
    public bool HasNamedVectors { get; set; }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
