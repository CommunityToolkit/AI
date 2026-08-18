// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.AzureDocumentDB;

/// <summary>
/// Options when creating a <see cref="DocumentDBVectorStore"/>
/// </summary>
public sealed class DocumentDBVectorStoreOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentDBVectorStoreOptions"/> class.
    /// </summary>
    public DocumentDBVectorStoreOptions()
    {
    }

    internal DocumentDBVectorStoreOptions(DocumentDBVectorStoreOptions? source)
    {
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
