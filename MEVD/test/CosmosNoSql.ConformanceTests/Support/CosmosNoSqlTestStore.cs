// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using CommunityToolkit.VectorData.CosmosNoSql;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.VectorData;
using Testcontainers.CosmosDb;
using VectorData.ConformanceTests.Support;

namespace CosmosNoSql.ConformanceTests.Support;

#pragma warning disable CA1001 // Type owns disposable fields but is not disposable

/// <param name="uniqueDatabaseName">The emulator does not support running multiple queries simultaneously, so we create a unique database for each test store instance.</param>
internal sealed class CosmosNoSqlTestStore(string uniqueDatabaseName) : TestStore
{
    private CosmosDbContainer? _container;
    private CosmosClient? _client;
    private Database? _database;
    private string? _connectionString;
    

    public bool UsesLocalEmulator => _container is not null;

    public override string DefaultIndexKind => "DiskAnn";

    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("Cosmos DB test store has not been started.");

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = new CosmosNoSqlTestJsonNamingPolicy()
    };

    public Database Database => _database ?? throw new InvalidOperationException("Cosmos DB test store has not been started.");

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
            _container ??= new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest")
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

        _database = await CreateDatabaseIfNotExistsAsync().ConfigureAwait(false);
        DefaultVectorStore = new CosmosNoSqlVectorStore(_database, new() { JsonSerializerOptions = SerializerOptions });
    }

    protected override async Task StopAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    private async Task<Database> CreateDatabaseIfNotExistsAsync()
    {
        TimeSpan maximumRetryDuration = TimeSpan.FromMinutes(1);
        TimeSpan fallbackRetryDelay = TimeSpan.FromSeconds(1);
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return await _client!.CreateDatabaseIfNotExistsAsync(uniqueDatabaseName).ConfigureAwait(false);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                TimeSpan remainingRetryDuration = maximumRetryDuration - stopwatch.Elapsed;
                if (remainingRetryDuration <= TimeSpan.Zero)
                {
                    throw;
                }

                TimeSpan? retryAfter = ex.RetryAfter;
                TimeSpan retryDelay = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
                    ? retryAfter.Value
                    : fallbackRetryDelay;
                if (retryDelay > remainingRetryDuration)
                {
                    retryDelay = remainingRetryDuration;
                }

                await Task.Delay(retryDelay).ConfigureAwait(false);
            }
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
