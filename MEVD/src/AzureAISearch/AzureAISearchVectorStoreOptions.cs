// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.AzureAISearch;

/// <summary>
/// Options when creating a <see cref="AzureAISearchVectorStore"/>.
/// </summary>
public sealed class AzureAISearchVectorStoreOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAISearchVectorStoreOptions"/> class.
    /// </summary>
    public AzureAISearchVectorStoreOptions()
    {
    }

    internal AzureAISearchVectorStoreOptions(AzureAISearchVectorStoreOptions? source)
    {
        JsonSerializerOptions = source?.JsonSerializerOptions;
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the JSON serializer options to use when converting between the data model and the Azure AI Search record.
    /// Note that when using the default mapper and you are constructing your own <see cref="SearchIndexClient"/>, you will need
    /// to provide the same set of <see cref="System.Text.Json.JsonSerializerOptions"/> both here and when constructing the <see cref="SearchIndexClient"/>.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
