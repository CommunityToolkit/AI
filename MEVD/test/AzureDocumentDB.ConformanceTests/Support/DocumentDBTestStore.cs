// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Security;
using System.Security.Authentication;
using CommunityToolkit.VectorData.AzureDocumentDB;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Bson;
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

    public MongoClient Client => _client ?? throw new InvalidOperationException("Not initialized");
    public IMongoDatabase Database => _database ?? throw new InvalidOperationException("Not initialized");

    public override string DefaultIndexKind => Microsoft.Extensions.VectorData.IndexKind.IvfFlat;

    public override string DefaultDistanceFunction => Microsoft.Extensions.VectorData.DistanceFunction.CosineDistance;

    public DocumentDBVectorStore GetVectorStore(DocumentDBVectorStoreOptions options)
        => new(Database, options);

    private DocumentDBTestStore()
    {
    }

    protected override async Task StartAsync()
    {
        await _container.StartAsync();
        string connectionString = $"mongodb://{Username}:{Password}@{_container.Hostname}:{_container.GetMappedPublicPort(DocumentDBPort)}/?tls=true";

        MongoClientSettings settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.SslSettings = new SslSettings
        {
            EnabledSslProtocols = SslProtocols.Tls12,
            ServerCertificateValidationCallback = static (_, _, _, _) => true,
        };

        _client = new MongoClient(settings);
        await WaitForMongoServiceAsync(_client, TimeSpan.FromMinutes(5));
        _database = _client.GetDatabase("VectorSearchTests");
        DefaultVectorStore = new DocumentDBVectorStore(_database);
    }

    protected override Task StopAsync()
    {
        _client?.Dispose();
        return _container.StopAsync();
    }

    private async Task WaitForMongoServiceAsync(MongoClient client, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        throw new TimeoutException($"Timed out waiting for the DocumentDB container to accept MongoDB connections. Last error: {lastException?.Message}", lastException);
    }
}
