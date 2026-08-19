// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.AzureCosmosDB;

/// <summary>
/// Options when creating a <see cref="CosmosDBCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class CosmosDBCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly CosmosDBCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDBCollectionOptions"/> class.
    /// </summary>
    public CosmosDBCollectionOptions()
    {
    }

    internal CosmosDBCollectionOptions(CosmosDBCollectionOptions? source) : base(source)
    {
        this.JsonSerializerOptions = source?.JsonSerializerOptions;
        this.PartitionKeyProperties = source?.PartitionKeyProperties is null ? null : [.. source.PartitionKeyProperties];
        this.IndexingMode = source?.IndexingMode ?? Default.IndexingMode;
        this.Automatic = source?.Automatic ?? Default.Automatic;
    }

    /// <summary>
    /// Gets or sets the JSON serializer options to use when converting between the data model and the Azure CosmosDB NoSQL record.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the property names to use as partition key components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Selecting a partition key is critical for performance and scalability. Choose properties with high cardinality
    /// that evenly distribute requests. See <see href="https://learn.microsoft.com/azure/cosmos-db/partitioning-overview"/> for guidance.
    /// </para>
    /// <para>
    /// When <see langword="null" /> (the default), the key property (document ID) is automatically used as the partition key - a common
    /// Cosmos DB strategy; in this mode, the collection key type must be <see cref="string"/> or <see cref="System.Guid"/>.
    /// To use a different partition key (or hierarchical partition keys), specify the key properties here and use
    /// <see cref="CosmosDBKey"/> as the key type.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? PartitionKeyProperties { get; set; }

    /// <summary>
    /// Specifies the indexing mode in the Azure Cosmos DB service.
    /// More information here: <see href="https://learn.microsoft.com/azure/cosmos-db/index-policy#indexing-mode"/>.
    /// </summary>
    /// <remarks>
    /// Default is <see cref="IndexingMode.Consistent" />.
    /// </remarks>
    public IndexingMode IndexingMode { get; set; } = IndexingMode.Consistent;

    /// <summary>
    /// Gets or sets a value that indicates whether automatic indexing is enabled for a collection in the Azure Cosmos DB service.
    /// </summary>
    /// <remarks>
    /// Default is <see langword="true" />.
    /// </remarks>
    public bool Automatic { get; set; } = true;
}
