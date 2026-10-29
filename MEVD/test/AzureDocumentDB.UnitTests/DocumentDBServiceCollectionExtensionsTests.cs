// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommunityToolkit.VectorData.AzureDocumentDB;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace AzureDocumentDB.UnitTests;

public sealed class DocumentDBServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void VectorStoreGetsEmbeddingGeneratorFromEachScope(ServiceLifetime lifetime)
    {
        // Arrange
        var resolvedGenerators = 0;
        var services = CreateServices(() => resolvedGenerators++);
        services.AddDocumentDBVectorStore(lifetime: lifetime);

        // Act
        ResolveInTwoScopes<DocumentDBVectorStore>(services);

        // Assert
        Assert.Equal(2, resolvedGenerators);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void VectorStoreWithConnectionStringGetsEmbeddingGeneratorFromEachScope(ServiceLifetime lifetime)
    {
        // Arrange
        var resolvedGenerators = 0;
        var services = CreateServices(() => resolvedGenerators++);
        services.AddDocumentDBVectorStore("mongodb://localhost", "database", lifetime: lifetime);

        // Act
        ResolveInTwoScopes<DocumentDBVectorStore>(services);

        // Assert
        Assert.Equal(2, resolvedGenerators);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void CollectionGetsEmbeddingGeneratorFromEachScope(ServiceLifetime lifetime)
    {
        // Arrange
        var resolvedGenerators = 0;
        var services = CreateServices(() => resolvedGenerators++);
        services.AddDocumentDBCollection<DocumentDBHotelModel>("collection", lifetime: lifetime);

        // Act
        ResolveInTwoScopes<DocumentDBCollection<string, DocumentDBHotelModel>>(services);

        // Assert
        Assert.Equal(2, resolvedGenerators);
    }

    private static ServiceCollection CreateServices(Action onGeneratorResolved)
    {
        var database = new Mock<IMongoDatabase>();
        database
            .Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(Mock.Of<IMongoCollection<BsonDocument>>());

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
        using var serviceProvider = services.BuildServiceProvider();

        for (var i = 0; i < 2; i++)
        {
            using var scope = serviceProvider.CreateScope();
            scope.ServiceProvider.GetRequiredService<TService>();
        }
    }
}
