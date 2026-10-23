// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using CommunityToolkit.VectorData.Milvus;
using Milvus.ConformanceTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Xunit;
namespace Milvus.ConformanceTests;
public sealed class MilvusDatabaseTests : IClassFixture<MilvusFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostRegistrationUsesSelectedDatabase(bool apiKey)
    {
        var server = MilvusTestStore.Instance;
        var database = "ct_db_" + Guid.NewGuid().ToString("N");
        var name = "records_" + Guid.NewGuid().ToString("N");
        await server.Client.CreateDatabaseAsync(database);
        try
        {
            var services = new ServiceCollection().AddMilvusVectorStore(server.Host, server.Port, database: database,
                apiKey: apiKey ? "root:Milvus" : null, username: apiKey ? null : "root", password: apiKey ? null : "Milvus");
            using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<MilvusVectorStore>();
            using var collection = store.GetCollection<string, MilvusRoundTripTests.Record>(name);
            try
            {
                await collection.EnsureCollectionExistsAsync();
                Assert.True(await collection.CollectionExistsAsync());
                Assert.False(await server.Client.HasCollectionAsync(name));
                Assert.Equal(database, Assert.IsType<VectorStoreMetadata>(store.GetService(typeof(VectorStoreMetadata))).VectorStoreName);
                Assert.Equal(database, Assert.IsType<VectorStoreCollectionMetadata>(collection.GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
            }
            finally
            {
                await collection.EnsureCollectionDeletedAsync();
            }
        }
        finally
        {
            await server.Client.DropDatabaseAsync(database);
        }
    }
}
