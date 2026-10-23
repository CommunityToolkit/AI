// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusCollectionTests
{
    [Fact]
    public async Task EmptyBatchesDoNotContactServer()
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var collection = new MilvusCollection<Guid, MilvusTestRecord>(client, "records", ownsClient: false);
        await collection.UpsertAsync(Array.Empty<MilvusTestRecord>());
        await collection.DeleteAsync(Array.Empty<Guid>());
        Assert.Empty(await collection.GetAsync(Array.Empty<Guid>()).ToListAsync());
    }

    [Fact]
    public void StoreExposesUnderlyingClientAndMetadata()
    {
        using var client = new MilvusClient("localhost");
        using var store = new MilvusVectorStore(client, ownsClient: false);
        Assert.Same(client, store.GetService(typeof(MilvusClient)));
        Assert.Equal("milvus", Assert.IsType<VectorStoreMetadata>(store.GetService(typeof(VectorStoreMetadata))).VectorStoreSystemName);
        Assert.Null(store.GetService(typeof(MilvusClient), "key"));
        using var collection = store.GetCollection<Guid, MilvusTestRecord>("records");
        Assert.Same(client, collection.GetService(typeof(MilvusClient)));
        Assert.Null(collection.GetService(typeof(IKeywordHybridSearchable<MilvusTestRecord>)));
    }

    [Fact]
    public void DependencyInjectionResolvesKeyedAndUnkeyedCollections()
    {
        using var client = new MilvusClient("localhost");
        var services = new ServiceCollection().AddSingleton(client)
            .AddMilvusVectorStore()
            .AddKeyedMilvusVectorStore("store")
            .AddMilvusCollection<Guid, MilvusTestRecord>("records")
            .AddKeyedMilvusCollection<Guid, MilvusTestRecord>("collection", "other_records");
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<MilvusVectorStore>(), provider.GetRequiredService<VectorStore>());
        Assert.Same(provider.GetRequiredKeyedService<MilvusVectorStore>("store"), provider.GetRequiredKeyedService<VectorStore>("store"));
        Assert.Equal("records", provider.GetRequiredService<VectorStoreCollection<Guid, MilvusTestRecord>>().Name);
        Assert.Equal("other_records", provider.GetRequiredKeyedService<VectorStoreCollection<Guid, MilvusTestRecord>>("collection").Name);
    }

    [Fact]
    public async Task RejectsInvalidSearchBeforeContactingServer()
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var collection = new MilvusCollection<Guid, MilvusTestRecord>(client, "records", ownsClient: false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await collection.SearchAsync(new float[] { 1, 2, 3 }, 0).ToListAsync());
        await Assert.ThrowsAsync<ArgumentException>(async () => await collection.SearchAsync(new float[] { 1 }, 1).ToListAsync());
    }

    [Fact]
    public void DynamicCollectionRequiresDefinition()
    {
        using var client = new MilvusClient("localhost");
        Assert.Throws<ArgumentException>(() => new MilvusDynamicCollection(client, "records", ownsClient: false, new()));
        Assert.Throws<NotSupportedException>(() => new MilvusCollection<object, Dictionary<string, object?>>(client, "records", ownsClient: false));
    }
}
