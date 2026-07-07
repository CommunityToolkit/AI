// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using CommunityToolkit.VectorData.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VectorData.ConformanceTests;

namespace Cosmos.ConformanceTests;

public sealed class CosmosNoSqlDependencyInjectionTests
    : DependencyInjectionTests<CosmosNoSqlVectorStore, CosmosNoSqlCollection<string, DependencyInjectionTests<string>.Record>, string, DependencyInjectionTests<string>.Record>
{
    private const string TestConnectionString = "AccountEndpoint=https://localhost:8081;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates
    {
        get
        {
            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddCosmosNoSqlVectorStore(TestConnectionString, nameof(CosmosNoSqlDependencyInjectionTests), lifetime: lifetime)
                : services.AddKeyedCosmosNoSqlVectorStore(serviceKey, TestConnectionString, nameof(CosmosNoSqlDependencyInjectionTests), lifetime: lifetime);
        }
    }

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates
    {
        get
        {
            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddCosmosNoSqlCollection<string, Record>(name, TestConnectionString, nameof(CosmosNoSqlDependencyInjectionTests), lifetime: lifetime)
                : services.AddKeyedCosmosNoSqlCollection<string, Record>(serviceKey, name, TestConnectionString, nameof(CosmosNoSqlDependencyInjectionTests), lifetime: lifetime);
        }
    }

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey)
    {
    }
}
