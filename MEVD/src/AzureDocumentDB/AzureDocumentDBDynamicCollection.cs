// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MongoDB.Driver;


namespace CommunityToolkit.VectorData.AzureDocumentDB;

/// <summary>
/// Represents a collection of vector store records in a Mongo database, mapped to a dynamic <c>Dictionary&lt;string, object?&gt;</c>.
/// </summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public sealed class AzureDocumentDBDynamicCollection : AzureDocumentDBCollection<object, Dictionary<string, object?>>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureDocumentDBDynamicCollection"/> class.
    /// </summary>
    /// <param name="mongoDatabase"><see cref="IMongoDatabase"/> that can be used to manage the collections in MongoDB.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    public AzureDocumentDBDynamicCollection(IMongoDatabase mongoDatabase, string name, AzureDocumentDBCollectionOptions options)
        : base(
            mongoDatabase,
            name,
            static options => new MongoModelBuilder()
                .BuildDynamic(
                    options.Definition ?? throw new ArgumentException("Definition is required for dynamic collections"),
                    options.EmbeddingGenerator),
            options)
    {
    }
}
