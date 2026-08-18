// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.AzureDocumentDB;
using MongoDB.Driver;
using VectorData.ConformanceTests.Support;

namespace AzureDocumentDB.ConformanceTests.Support;

#pragma warning disable CA1001
public sealed class DocumentDBTestStore : TestStore
#pragma warning restore CA1001
{
    public static DocumentDBTestStore Instance { get; } = new();

    private MongoClient? _client;
    private IMongoDatabase? _database;

    public MongoClient Client => this._client ?? throw new InvalidOperationException("Not initialized");
    public IMongoDatabase Database => this._database ?? throw new InvalidOperationException("Not initialized");

    public override string DefaultIndexKind => Microsoft.Extensions.VectorData.IndexKind.IvfFlat;

    public override string DefaultDistanceFunction => Microsoft.Extensions.VectorData.DistanceFunction.CosineDistance;

    public DocumentDBVectorStore GetVectorStore(DocumentDBVectorStoreOptions options)
        => new(this.Database, options);

    private DocumentDBTestStore()
    {
    }

    protected override Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(DocumentDBTestEnvironment.ConnectionString))
        {
            throw new InvalidOperationException("Connection string is not configured, set the AzureDocumentDB:ConnectionString environment variable");
        }

        this._client = new MongoClient(DocumentDBTestEnvironment.ConnectionString);
        this._database = this._client.GetDatabase("VectorSearchTests");
        this.DefaultVectorStore = new DocumentDBVectorStore(this._database);

        return Task.CompletedTask;
    }
}
