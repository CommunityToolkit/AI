// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.CosmosDocumentDB;
using MongoDB.Driver;
using VectorData.ConformanceTests.Support;

namespace CosmosDocumentDB.ConformanceTests.Support;

#pragma warning disable CA1001
public sealed class CosmosDocumentDBTestStore : TestStore
#pragma warning restore CA1001
{
    public static CosmosDocumentDBTestStore Instance { get; } = new();

    private MongoClient? _client;
    private IMongoDatabase? _database;

    public MongoClient Client => this._client ?? throw new InvalidOperationException("Not initialized");
    public IMongoDatabase Database => this._database ?? throw new InvalidOperationException("Not initialized");

    public override string DefaultIndexKind => Microsoft.Extensions.VectorData.IndexKind.IvfFlat;

    public override string DefaultDistanceFunction => Microsoft.Extensions.VectorData.DistanceFunction.CosineDistance;

    public CosmosDocumentDBVectorStore GetVectorStore(CosmosDocumentDBVectorStoreOptions options)
        => new(this.Database, options);

    private CosmosDocumentDBTestStore()
    {
    }

    protected override Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(CosmosDocumentDBTestEnvironment.ConnectionString))
        {
            throw new InvalidOperationException("Connection string is not configured, set the CosmosDocumentDB:ConnectionString environment variable");
        }

        this._client = new MongoClient(CosmosDocumentDBTestEnvironment.ConnectionString);
        this._database = this._client.GetDatabase("VectorSearchTests");
        this.DefaultVectorStore = new CosmosDocumentDBVectorStore(this._database);

        return Task.CompletedTask;
    }
}
