// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.AzureCosmosDB;

/// <summary>
/// Represents a collection of vector store records in a Cosmos database, mapped to a dynamic <c>Dictionary&lt;string, object?&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This collection accepts keys of type <see cref="object"/>, but only <see cref="CosmosKey"/> instances
/// are supported at runtime. Passing any other key type will result in an <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public sealed class CosmosDynamicCollection : CosmosCollection<object, Dictionary<string, object?>>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDynamicCollection"/> class.
    /// </summary>
    /// <param name="database"><see cref="Database"/> that can be used to manage the collections in Azure Cosmos NoSQL.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    [RequiresUnreferencedCode("The Cosmos NoSQL provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Cosmos NoSQL provider is currently incompatible with NativeAOT.")]
    public CosmosDynamicCollection(Database database, string name, CosmosCollectionOptions options)
        : this(
            new(database.Client, ownsClient: false),
            _ => database,
            name,
            options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDynamicCollection"/> class.
    /// </summary>
    /// <param name="connectionString">Connection string required to connect to Azure Cosmos NoSQL.</param>
    /// <param name="databaseName">Database name for Azure Cosmos NoSQL.</param>
    /// <param name="collectionName">The name of the collection that this <see cref="CosmosDynamicCollection"/> will access.</param>
    /// <param name="clientOptions">Optional configuration options for <see cref="CosmosClient"/>.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    [RequiresUnreferencedCode("The Cosmos NoSQL provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Cosmos NoSQL provider is currently incompatible with NativeAOT.")]
    public CosmosDynamicCollection(
        string connectionString,
        string databaseName,
        string collectionName,
        CosmosClientOptions? clientOptions = null,
        CosmosCollectionOptions? options = null)
        : this(
            new(new CosmosClient(connectionString, clientOptions), ownsClient: true),
            client => client.GetDatabase(databaseName),
            collectionName,
            options)
    {
        Throw.IfNullOrWhitespace(connectionString);
        Throw.IfNullOrWhitespace(databaseName);
        Throw.IfNullOrWhitespace(collectionName);
    }

    internal CosmosDynamicCollection(
        ClientWrapper clientWrapper,
        Func<CosmosClient, Database> databaseProvider,
        string name,
        CosmosCollectionOptions? options)
        : base(
            clientWrapper,
            databaseProvider,
            name,
            static options => new CosmosModelBuilder()
                .BuildDynamic(
                    options.Definition ?? throw new ArgumentException("Definition is required for dynamic collections"),
                    options.EmbeddingGenerator,
                    options.JsonSerializerOptions ?? JsonSerializerOptions.Default),
            options)
    {
    }
}
