// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.CosmosMongoDB;

/// <summary>
/// Options when creating a <see cref="CosmosMongoVectorStore"/>
/// </summary>
public sealed class CosmosMongoVectorStoreOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosMongoVectorStoreOptions"/> class.
    /// </summary>
    public CosmosMongoVectorStoreOptions()
    {
    }

    internal CosmosMongoVectorStoreOptions(CosmosMongoVectorStoreOptions? source)
    {
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
