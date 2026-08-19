// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.AzureCosmosDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VectorData.ConformanceTests;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosDBDependencyInjectionTests
    : DependencyInjectionTests<CosmosDBVectorStore, CosmosDBCollection<string, DependencyInjectionTests<string>.Record>, string, DependencyInjectionTests<string>.Record>
{
    private const string TestConnectionString = "AccountEndpoint=https://localhost:8081;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates
    {
        get
        {
            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddCosmosDBVectorStore(TestConnectionString, nameof(CosmosDBDependencyInjectionTests), lifetime: lifetime)
                : services.AddKeyedCosmosDBVectorStore(serviceKey, TestConnectionString, nameof(CosmosDBDependencyInjectionTests), lifetime: lifetime);
        }
    }

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates
    {
        get
        {
            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddCosmosDBCollection<string, Record>(name, TestConnectionString, nameof(CosmosDBDependencyInjectionTests), lifetime: lifetime)
                : services.AddKeyedCosmosDBCollection<string, Record>(serviceKey, name, TestConnectionString, nameof(CosmosDBDependencyInjectionTests), lifetime: lifetime);
        }
    }

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey)
    {
    }
}
