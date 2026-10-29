// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Moq;
using Xunit;

namespace CommunityToolkit.VectorData.AzureCosmosDB.UnitTests;

public sealed class CosmosServiceCollectionExtensionsTests
{
    private const string ConnectionString = "AccountEndpoint=https://localhost:8081;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void VectorStoreGetsEmbeddingGeneratorFromEachScope(ServiceLifetime lifetime)
    {
        int resolvedGenerators = 0;
        ServiceCollection services = CreateServices(() => resolvedGenerators++);
        services.AddCosmosVectorStore(lifetime: lifetime);

        ResolveInTwoScopes<CosmosVectorStore>(services);

        Assert.Equal(2, resolvedGenerators);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void VectorStoreWithConnectionStringGetsEmbeddingGeneratorFromEachScope(ServiceLifetime lifetime)
    {
        int resolvedGenerators = 0;
        ServiceCollection services = CreateServices(() => resolvedGenerators++);
        services.AddCosmosVectorStore(ConnectionString, "database", lifetime: lifetime);

        ResolveInTwoScopes<CosmosVectorStore>(services);

        Assert.Equal(2, resolvedGenerators);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void CollectionGetsEmbeddingGeneratorFromEachScope(ServiceLifetime lifetime)
    {
        int resolvedGenerators = 0;
        ServiceCollection services = CreateServices(() => resolvedGenerators++);
        services.AddCosmosCollection<string, Record>("collection", lifetime: lifetime);

        ResolveInTwoScopes<CosmosCollection<string, Record>>(services);

        Assert.Equal(2, resolvedGenerators);
    }

    private static ServiceCollection CreateServices(Action onGeneratorResolved)
    {
        Mock<CosmosClient> client = new();
        client.SetupGet(c => c.ClientOptions).Returns(new CosmosClientOptions { UseSystemTextJsonSerializerWithOptions = new() });
        Mock<Database> database = new();
        database.SetupGet(d => d.Client).Returns(client.Object);
        database.Setup(d => d.GetContainer(It.IsAny<string>())).Returns(new Mock<Container>().Object);

        ServiceCollection services = new();
        services.AddSingleton(database.Object);
        services.AddScoped<IEmbeddingGenerator>(_ =>
        {
            onGeneratorResolved();
            return new FakeEmbeddingGenerator();
        });

        return services;
    }

    private static void ResolveInTwoScopes<TService>(ServiceCollection services)
        where TService : notnull
    {
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        for (int i = 0; i < 2; i++)
        {
            using IServiceScope scope = serviceProvider.CreateScope();
            scope.ServiceProvider.GetRequiredService<TService>();
        }
    }

    public sealed class Record
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }
}
