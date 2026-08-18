// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.AzureDocumentDB;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Driver;
using VectorData.ConformanceTests.Support;

namespace AzureDocumentDB.ConformanceTests.Support;

#pragma warning disable CA1001
public sealed class DocumentDBTestStore : TestStore
#pragma warning restore CA1001
{
    private const string Username = "testuser";
    private const string Password = "TestPassword123!";
    private const ushort DocumentDBPort = 10260;

    public static DocumentDBTestStore Instance { get; } = new();

    private readonly IContainer _container = new ContainerBuilder("ghcr.io/microsoft/documentdb/documentdb-local:latest")
        .WithPortBinding(DocumentDBPort, assignRandomHostPort: true)
        .WithEnvironment("USERNAME", Username)
        .WithEnvironment("PASSWORD", Password)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilInternalTcpPortIsAvailable(DocumentDBPort, strategy => strategy.WithTimeout(TimeSpan.FromMinutes(5))))
        .Build();

    private MongoClient? _client;
    private IMongoDatabase? _database;
    private bool _useExternalInstance;

    public MongoClient Client => this._client ?? throw new InvalidOperationException("Not initialized");
    public IMongoDatabase Database => this._database ?? throw new InvalidOperationException("Not initialized");

    public override string DefaultIndexKind => Microsoft.Extensions.VectorData.IndexKind.IvfFlat;

    public override string DefaultDistanceFunction => Microsoft.Extensions.VectorData.DistanceFunction.CosineDistance;

    public DocumentDBVectorStore GetVectorStore(DocumentDBVectorStoreOptions options)
        => new(this.Database, options);

    private DocumentDBTestStore()
    {
    }

    protected override async Task StartAsync()
    {
        string connectionString;
        if (DocumentDBTestEnvironment.IsConnectionStringDefined)
        {
            connectionString = DocumentDBTestEnvironment.ConnectionString!;
            this._useExternalInstance = true;
        }
        else
        {
            await this._container.StartAsync();
            connectionString = $"mongodb://{Username}:{Password}@{this._container.Hostname}:{this._container.GetMappedPublicPort(DocumentDBPort)}/?tls=true&tlsAllowInvalidCertificates=true";
            this._useExternalInstance = false;
        }

        this._client = new MongoClient(connectionString);
        this._database = this._client.GetDatabase("VectorSearchTests");
        this.DefaultVectorStore = new DocumentDBVectorStore(this._database);
    }

    protected override Task StopAsync()
    {
        return this._useExternalInstance
            ? Task.CompletedTask
            : this._container.StopAsync();
    }
}
