// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// Options when creating a <see cref="ChromaVectorStore"/>.
/// </summary>
public sealed class ChromaVectorStoreOptions
{
    internal static readonly ChromaVectorStoreOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaVectorStoreOptions"/> class.
    /// </summary>
    public ChromaVectorStoreOptions()
    {
    }

    internal ChromaVectorStoreOptions(ChromaVectorStoreOptions? source)
    {
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vector embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
