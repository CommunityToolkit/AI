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

    private static readonly CosmosDbContainer s_container = new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest")
        .Build();

    private static readonly SemaphoreSlim s_initLock = new(1, 1);
    private static bool s_initialized;

    private CosmosClient? _client;
    private Database? _database;
    private bool _useExternalInstance;
    private string? _connectionString;

    public static CosmosNoSqlTestStore Instance { get; } = new();

    public override string DefaultIndexKind => "DiskAnn";

    public string ConnectionString => this._connectionString ?? throw new InvalidOperationException("Cosmos DB test store has not been started.");

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = new CosmosNoSqlTestJsonNamingPolicy()
    };

    public Database Database => this._database ?? throw new InvalidOperationException("Cosmos DB test store has not been started.");

    private CosmosNoSqlTestStore()
    {
    }

    public override VectorStoreCollection<TKey, TRecord> CreateCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition definition)
        => new CosmosNoSqlCollection<TKey, TRecord>(
            this.Database,
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
            this.Database,
            name,
            new()
            {
                Definition = definition,
                JsonSerializerOptions = SerializerOptions,
            });

    protected override async Task StartAsync()
    {
        if (s_initialized)
        {
            return;
        }

        await s_initLock.WaitAsync();
        try
        {
            if (s_initialized)
            {
                return;
            }

            if (CosmosNoSqlTestEnvironment.IsConnectionStringDefined)
            {
                this._connectionString = CosmosNoSqlTestEnvironment.ConnectionString!;
                this._useExternalInstance = true;
            }
            else
            {
                await s_container.StartAsync();
                this._connectionString = s_container.GetConnectionString();
                this._useExternalInstance = false;
            }

            this._client = new CosmosClient(
                this._connectionString,
                new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Gateway,
                    LimitToEndpoint = true,
                    RequestTimeout = TimeSpan.FromSeconds(10),
                    UseSystemTextJsonSerializerWithOptions = JsonSerializerOptions.Default,
                    HttpClientFactory = !this._useExternalInstance
                        ? () => s_container.HttpClient
                        : null,
                });

            this._database = await this._client.CreateDatabaseIfNotExistsAsync(DatabaseName).ConfigureAwait(false);
            this.DefaultVectorStore = new CosmosNoSqlVectorStore(this._database, new() { JsonSerializerOptions = SerializerOptions });
            s_initialized = true;
        }
        finally
        {
            s_initLock.Release();
        }
    }

    protected override Task StopAsync()
    {
        // Don't stop the container here - it's shared across multiple test fixtures.
        // The Testcontainers resource reaper will clean it up when the test process exits.
        return Task.CompletedTask;
    }

    private sealed class CosmosNoSqlTestJsonNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
            => name is "Vector" or "Embedding"
                ? "vector"
                : JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}
