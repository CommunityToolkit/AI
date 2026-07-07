// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using CommunityToolkit.VectorData.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.VectorData;
using Testcontainers.CosmosDb;
using VectorData.ConformanceTests.Support;

namespace Cosmos.ConformanceTests.Support;

#pragma warning disable CA1001 // Type owns disposable fields but is not disposable

internal sealed class CosmosNoSqlTestStore : TestStore
{
    public const string DatabaseName = "VectorDataConformanceTests";

    private CosmosDbContainer? _container;
    private CosmosClient? _client;
    private Database? _database;
    private string? _connectionString;

    public static CosmosNoSqlTestStore Instance { get; } = new();

    public bool UsesLocalEmulator => _container is not null;

    public override string DefaultIndexKind => "DiskAnn";

    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("Cosmos DB test store has not been started.");

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = new CosmosNoSqlTestJsonNamingPolicy()
    };

    public Database Database => _database ?? throw new InvalidOperationException("Cosmos DB test store has not been started.");

    private CosmosNoSqlTestStore()
    {
    }

    public override VectorStoreCollection<TKey, TRecord> CreateCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition definition)
        => new CosmosNoSqlCollection<TKey, TRecord>(
            Database,
            name,
            new()
            {
                Definition = definition,
                JsonSerializerOptions = SerializerOptions,
            });

    public override VectorStoreCollection<object, Dictionary<string, object?>> CreateDynamicCollection(
        string name,
        VectorStoreCollectionDefinition definition)
        => new CosmosNoSqlDynamicCollection(
            Database,
            name,
            new()
            {
                Definition = definition,
                JsonSerializerOptions = SerializerOptions,
            });

    protected override async Task StartAsync()
    {
        if (CosmosNoSqlTestEnvironment.IsConnectionStringDefined)
        {
            _connectionString = CosmosNoSqlTestEnvironment.ConnectionString!;
        }
        else
        {
            _container = new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest")
                .WithEnvironment("QUERY_BUFFER_SIZE_KB", "65536")
                .Build();

            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        CosmosClientOptions clientOptions = new()
        {
            ConnectionMode = ConnectionMode.Gateway,
            LimitToEndpoint = true,
            RequestTimeout = TimeSpan.FromSeconds(10),
            UseSystemTextJsonSerializerWithOptions = JsonSerializerOptions.Default,
            HttpClientFactory = _container is not null
                ? () => _container.HttpClient
                : null,
        };

        _client = new CosmosClient(_connectionString, clientOptions);

        _database = await _client.CreateDatabaseIfNotExistsAsync(DatabaseName).ConfigureAwait(false);
        DefaultVectorStore = new CosmosNoSqlVectorStore(_database, new() { JsonSerializerOptions = SerializerOptions });
    }

    protected override async Task StopAsync()
    {
        if (_container is not null)
        {
            // Instead of stopping the container, we dispose it so every test class gets a brand new container.
            // This is because the emulator does not handle running multiple tests well.
            await _container.DisposeAsync();
        }
    }

    private sealed class CosmosNoSqlTestJsonNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
            => name is "Vector" or "Embedding"
                ? "vector"
                : JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}
