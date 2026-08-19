// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.AzureCosmosDB;

/// <summary>
/// Options when creating a <see cref="CosmosDBVectorStore"/>.
/// </summary>
public sealed class CosmosDBVectorStoreOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDBVectorStoreOptions"/> class.
    /// </summary>
    public CosmosDBVectorStoreOptions()
    {
    }

    internal CosmosDBVectorStoreOptions(CosmosDBVectorStoreOptions? source)
    {
        this.JsonSerializerOptions = source?.JsonSerializerOptions;
        this.EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the JSON serializer options to use when converting between the data model and the Azure CosmosDB NoSQL record.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
