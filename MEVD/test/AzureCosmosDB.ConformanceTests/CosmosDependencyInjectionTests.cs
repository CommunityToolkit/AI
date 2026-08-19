// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.AzureCosmosDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VectorData.ConformanceTests;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosDependencyInjectionTests
    : DependencyInjectionTests<CosmosVectorStore, CosmosCollection<string, DependencyInjectionTests<string>.Record>, string, DependencyInjectionTests<string>.Record>
{
    private const string TestConnectionString = "AccountEndpoint=https://localhost:8081;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates
    {
        get
        {
            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddCosmosVectorStore(TestConnectionString, nameof(CosmosDependencyInjectionTests), lifetime: lifetime)
                : services.AddKeyedCosmosVectorStore(serviceKey, TestConnectionString, nameof(CosmosDependencyInjectionTests), lifetime: lifetime);
        }
    }

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates
    {
        get
        {
            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddCosmosCollection<string, Record>(name, TestConnectionString, nameof(CosmosDependencyInjectionTests), lifetime: lifetime)
                : services.AddKeyedCosmosCollection<string, Record>(serviceKey, name, TestConnectionString, nameof(CosmosDependencyInjectionTests), lifetime: lifetime);
        }
    }

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey)
    {
    }
}
