// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.CosmosNoSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VectorData.ConformanceTests;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlDependencyInjectionTests
    : DependencyInjectionTests<CosmosNoSqlVectorStore, CosmosNoSqlCollection<string, DependencyInjectionTests<string>.Record>, string, DependencyInjectionTests<string>.Record>
{
    private const string TestConnectionString = "AccountEndpoint=https://localhost:8081;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

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
