// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Azure.Search.Documents.Indexes;

namespace CommunityToolkit.VectorData.AzureAISearch;

/// <summary>
/// Represents a collection of vector store records in a AzureAISearch database, mapped to a dynamic <c>Dictionary&lt;string, object?&gt;</c>.
/// </summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public sealed class AzureAISearchDynamicCollection : AzureAISearchCollection<object, Dictionary<string, object?>>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAISearchDynamicCollection"/> class.
    /// </summary>
    /// <param name="searchIndexClient">Azure AI Search client that can be used to manage the list of indices in an Azure AI Search Service.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    [RequiresUnreferencedCode("The Azure AI Search provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Azure AI Search provider is currently incompatible with NativeAOT.")]
    public AzureAISearchDynamicCollection(SearchIndexClient searchIndexClient, string name, AzureAISearchCollectionOptions options)
        : base(
            searchIndexClient,
            name,
            static options => new AzureAISearchDynamicModelBuilder().BuildDynamic(
                options.Definition ?? throw new ArgumentException("Definition is required for dynamic collections"),
                options.EmbeddingGenerator),
            options)
    {
    }
}
