// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.CosmosDocumentDB;

/// <summary>
/// Options when creating a <see cref="CosmosDocumentDBVectorStore"/>
/// </summary>
public sealed class CosmosDocumentDBVectorStoreOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDocumentDBVectorStoreOptions"/> class.
    /// </summary>
    public CosmosDocumentDBVectorStoreOptions()
    {
    }

    internal CosmosDocumentDBVectorStoreOptions(CosmosDocumentDBVectorStoreOptions? source)
    {
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
