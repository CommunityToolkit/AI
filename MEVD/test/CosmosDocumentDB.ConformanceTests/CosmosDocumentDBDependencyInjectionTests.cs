// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.VectorData.CosmosDocumentDB;
using MongoDB.Driver;
using VectorData.ConformanceTests;
using Xunit;

namespace CosmosDocumentDB.ConformanceTests;

public class CosmosDocumentDBDependencyInjectionTests
    : DependencyInjectionTests<CosmosDocumentDBVectorStore, CosmosDocumentDBCollection<string, DependencyInjectionTests<string>.Record>, string, DependencyInjectionTests<string>.Record>
{
    protected const string ConnectionString = "mongodb://localhost:27017";
    protected const string DatabaseName = "dbName";

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey = null)
        => configuration.AddInMemoryCollection(
        [
            new(CreateConfigKey("CosmosDocumentDB", serviceKey, "ConnectionString"), ConnectionString),
            new(CreateConfigKey("CosmosDocumentDB", serviceKey, "DatabaseName"), DatabaseName),
        ]);

    private static string ConnectionStringProvider(IServiceProvider sp)
        => sp.GetRequiredService<IConfiguration>().GetRequiredSection("CosmosDocumentDB:ConnectionString").Value!;

    private static string ConnectionStringProvider(IServiceProvider sp, object serviceKey)
        => sp.GetRequiredService<IConfiguration>().GetRequiredSection(CreateConfigKey("CosmosDocumentDB", serviceKey, "ConnectionString")).Value!;

    private static string DatabaseNameProvider(IServiceProvider sp)
        => sp.GetRequiredService<IConfiguration>().GetRequiredSection("CosmosDocumentDB:DatabaseName").Value!;

    private static string DatabaseNameProvider(IServiceProvider sp, object serviceKey)
        => sp.GetRequiredService<IConfiguration>().GetRequiredSection(CreateConfigKey("CosmosDocumentDB", serviceKey, "DatabaseName")).Value!;

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates
    {
        get
        {
            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services
                    .AddSingleton<MongoClient>(sp => new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString)))
                    .AddSingleton<IMongoDatabase>(sp => sp.GetRequiredService<MongoClient>().GetDatabase(DatabaseName))
                    .AddCosmosDocumentDBCollection<Record>(name, lifetime: lifetime)
                : services
                    .AddSingleton<MongoClient>(sp => new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString)))
                    .AddSingleton<IMongoDatabase>(sp => sp.GetRequiredService<MongoClient>().GetDatabase(DatabaseName))
                    .AddKeyedCosmosDocumentDBCollection<Record>(serviceKey, name, lifetime: lifetime);

            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddCosmosDocumentDBCollection<Record>(
                    name, ConnectionString, DatabaseName, lifetime: lifetime)
                : services.AddKeyedCosmosDocumentDBCollection<Record>(
                    serviceKey, name, ConnectionString, DatabaseName, lifetime: lifetime);

            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddCosmosDocumentDBCollection<Record>(
                    name, ConnectionStringProvider, DatabaseNameProvider, lifetime: lifetime)
                : services.AddKeyedCosmosDocumentDBCollection<Record>(
                    serviceKey, name, sp => ConnectionStringProvider(sp, serviceKey), sp => DatabaseNameProvider(sp, serviceKey), lifetime: lifetime);
        }
    }

    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates
    {
        get
        {
            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddCosmosDocumentDBVectorStore(
                    ConnectionString, DatabaseName, lifetime: lifetime)
                : services.AddKeyedCosmosDocumentDBVectorStore(
                    serviceKey, ConnectionString, DatabaseName, lifetime: lifetime);

            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services
                    .AddSingleton<MongoClient>(sp => new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString)))
                    .AddSingleton<IMongoDatabase>(sp => sp.GetRequiredService<MongoClient>().GetDatabase(DatabaseName))
                    .AddCosmosDocumentDBVectorStore(lifetime: lifetime)
                : services
                    .AddSingleton<MongoClient>(sp => new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString)))
                    .AddSingleton<IMongoDatabase>(sp => sp.GetRequiredService<MongoClient>().GetDatabase(DatabaseName))
                    .AddKeyedCosmosDocumentDBVectorStore(serviceKey, lifetime: lifetime);
        }
    }

    [Fact]
    public void ConnectionStringProviderCantBeNull()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddCosmosDocumentDBCollection<Record>(
            name: "notNull", connectionStringProvider: null!, databaseNameProvider: DatabaseNameProvider));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedCosmosDocumentDBCollection<Record>(
            serviceKey: "notNull", name: "notNull", connectionStringProvider: null!, databaseNameProvider: DatabaseNameProvider));
    }

    [Fact]
    public void DatabaseNameProviderCantBeNull()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddCosmosDocumentDBCollection<Record>(
            name: "notNull", connectionStringProvider: ConnectionStringProvider, databaseNameProvider: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedCosmosDocumentDBCollection<Record>(
            serviceKey: "notNull", name: "notNull", connectionStringProvider: ConnectionStringProvider, databaseNameProvider: null!));
    }

    [Fact]
    public void ConnectionStringCantBeNullOrEmpty()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddCosmosDocumentDBVectorStore(connectionString: null!, DatabaseName));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedCosmosDocumentDBVectorStore(serviceKey: "notNull", connectionString: null!, DatabaseName));
        Assert.Throws<ArgumentNullException>(() => services.AddCosmosDocumentDBCollection<Record>(
            name: "notNull", connectionString: null!, DatabaseName));
        Assert.Throws<ArgumentException>(() => services.AddCosmosDocumentDBCollection<Record>(
            name: "notNull", connectionString: "", DatabaseName));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedCosmosDocumentDBCollection<Record>(
            serviceKey: "notNull", name: "notNull", connectionString: null!, DatabaseName));
        Assert.Throws<ArgumentException>(() => services.AddKeyedCosmosDocumentDBCollection<Record>(
            serviceKey: "notNull", name: "notNull", connectionString: "", DatabaseName));
    }

    [Fact]
    public void DatabaseNameCantBeNullOrEmpty()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddCosmosDocumentDBVectorStore(ConnectionString, databaseName: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedCosmosDocumentDBVectorStore(serviceKey: "notNull", ConnectionString, databaseName: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddCosmosDocumentDBCollection<Record>(
            name: "notNull", ConnectionString, databaseName: null!));
        Assert.Throws<ArgumentException>(() => services.AddCosmosDocumentDBCollection<Record>(
            name: "notNull", ConnectionString, databaseName: ""));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedCosmosDocumentDBCollection<Record>(
            serviceKey: "notNull", name: "notNull", ConnectionString, databaseName: null!));
        Assert.Throws<ArgumentException>(() => services.AddKeyedCosmosDocumentDBCollection<Record>(
            serviceKey: "notNull", name: "notNull", ConnectionString, databaseName: ""));
    }
}
