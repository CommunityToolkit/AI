// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.AzureDocumentDB;

/// <summary>
/// Options when creating a <see cref="AzureDocumentDBVectorStore"/>
/// </summary>
public sealed class AzureDocumentDBVectorStoreOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureDocumentDBVectorStoreOptions"/> class.
    /// </summary>
    public AzureDocumentDBVectorStoreOptions()
    {
    }

    internal AzureDocumentDBVectorStoreOptions(AzureDocumentDBVectorStoreOptions? source)
    {
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
