// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.Cosmos;
using Microsoft.Azure.Cosmos;
using VectorData.ConformanceTests.Support;

namespace Cosmos.ConformanceTests.Support;

internal sealed class CosmosNoSqlTestStore : TestStore
{
    public const string DatabaseName = "VectorDataConformanceTests";
    private const string EmulatorEndpointEnvironmentName = "COSMOSDBEMULATOR_ENDPOINT";
    private const string EmulatorKeyEnvironmentName = "COSMOSDBEMULATOR_KEY";
    private const string DefaultEmulatorEndpoint = "https://localhost:8081/";
    private const string DefaultEmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private CosmosClient? client;
    private Database? database;

    public static CosmosNoSqlTestStore Instance { get; } = new();

    public override string DefaultIndexKind => "Flat";

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = new CosmosNoSqlTestJsonNamingPolicy()
    };

    public Database Database => this.GetDatabase();

    public static string ConnectionString
    {
        get
        {
            string endpoint = Environment.GetEnvironmentVariable(EmulatorEndpointEnvironmentName) ?? DefaultEmulatorEndpoint;
            string key = Environment.GetEnvironmentVariable(EmulatorKeyEnvironmentName) ?? DefaultEmulatorKey;

            return $"AccountEndpoint={endpoint};AccountKey={key};DisableServerCertificateValidation=true";
        }
    }

    public override VectorStoreCollection<TKey, TRecord> CreateCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition definition)
        => new CosmosNoSqlCollection<TKey, TRecord>(
            this.GetDatabase(),
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
            this.GetDatabase(),
            name,
            new()
            {
                Definition = definition,
                JsonSerializerOptions = SerializerOptions,
            });

    protected override async Task StartAsync()
    {
        if (this.client is not null)
        {
            return;
        }

        AppDomain.CurrentDomain.SetData(
            "APP_CONFIG_FILE",
            RemoveExtendedPathPrefix(Path.Combine(AppContext.BaseDirectory, "testhost.dll.config")));

        this.client = new CosmosClient(
            ConnectionString,
            new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                LimitToEndpoint = true,
                RequestTimeout = TimeSpan.FromSeconds(10),
                UseSystemTextJsonSerializerWithOptions = JsonSerializerOptions.Default,
            });

        try
        {
            await this.client.GetDatabase(DatabaseName).DeleteStreamAsync().ConfigureAwait(false);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        this.database = await this.client.CreateDatabaseIfNotExistsAsync(DatabaseName).ConfigureAwait(false);
        this.DefaultVectorStore = new CosmosNoSqlVectorStore(this.database, new() { JsonSerializerOptions = SerializerOptions });
    }

    protected override async Task StopAsync()
    {
        if (this.database is not null)
        {
            await this.database.DeleteStreamAsync().ConfigureAwait(false);
        }

        this.database = null;
        this.client?.Dispose();
        this.client = null;
    }

    private Database GetDatabase()
        => this.database ?? throw new InvalidOperationException("Cosmos DB Emulator test store has not been started.");

    private static string RemoveExtendedPathPrefix(string path)
        => path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? path.Substring(4)
            : path;

    private sealed class CosmosNoSqlTestJsonNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
            => name is "Vector" or "Embedding"
                ? "vector"
                : JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}
