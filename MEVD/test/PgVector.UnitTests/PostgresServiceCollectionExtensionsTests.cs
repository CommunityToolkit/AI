// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.PgVector;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace PgVector.UnitTests;

public sealed class PostgresServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void VectorStoreGetsEmbeddingGeneratorFromEachScope(ServiceLifetime lifetime)
    {
        // Arrange
        var resolvedGenerators = 0;
        using var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test;");

        var services = new ServiceCollection();
        services.AddSingleton(dataSource);
        services.AddScoped<IEmbeddingGenerator>(_ =>
        {
            resolvedGenerators++;
            return new FakeEmbeddingGenerator();
        });
        services.AddPostgresVectorStore(lifetime: lifetime);

        // Act
        using (var serviceProvider = services.BuildServiceProvider())
        {
            for (var i = 0; i < 2; i++)
            {
                using var scope = serviceProvider.CreateScope();
                scope.ServiceProvider.GetRequiredService<PostgresVectorStore>();
            }
        }

        // Assert
        Assert.Equal(2, resolvedGenerators);
    }
}
