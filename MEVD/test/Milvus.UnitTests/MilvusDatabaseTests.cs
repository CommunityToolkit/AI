// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusDatabaseTests
{
    [Fact]
    public void OptionsHaveNoDatabase()
    {
        // The database is a setting of the Milvus client: the options of the store and of the collections do not carry it.
        Assert.Null(typeof(MilvusVectorStoreOptions).GetProperty("DatabaseName"));
        Assert.Null(typeof(MilvusCollectionOptions).GetProperty("DatabaseName"));
    }

    [Fact]
    public void SuppliedClientLeavesTheDatabaseOutOfTheMetadata()
    {
        // Milvus.Client does not expose the database it is configured with.
        using var client = new MilvusClient("localhost", database: "catalog");
        using var store = new MilvusVectorStore(client, ownsClient: false);
        Assert.Null(Assert.IsType<VectorStoreMetadata>(store.GetService(typeof(VectorStoreMetadata))).VectorStoreName);
        using var typed = store.GetCollection<Guid, MilvusTestRecord>("records");
        Assert.Null(Assert.IsType<VectorStoreCollectionMetadata>(typed.GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
        using var dynamic = store.GetDynamicCollection("records", new() { Properties =
            [new VectorStoreKeyProperty("Id", typeof(string)), new VectorStoreVectorProperty("Vector", typeof(float[]), 3)] });
        Assert.Null(Assert.IsType<VectorStoreCollectionMetadata>(dynamic.GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
        using var collection = new MilvusCollection<Guid, MilvusTestRecord>(client, "records", ownsClient: false);
        Assert.Null(Assert.IsType<VectorStoreCollectionMetadata>(collection.GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
    }

    [Fact]
    public void HostRegistrationsSupportDatabaseAndBothAuthenticationModes()
    {
        var services = new ServiceCollection()
            .AddMilvusVectorStore("localhost", database: "catalog", username: "root", password: "password")
            .AddKeyedMilvusVectorStore("key", "localhost", apiKey: "token", database: "catalog")
            .AddMilvusCollection<Guid, MilvusTestRecord>("records", "localhost", database: "catalog", username: "root", password: "password")
            .AddKeyedMilvusCollection<Guid, MilvusTestRecord>("key", "records", "localhost", apiKey: "token", database: "catalog");
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<VectorStore>();
        Assert.Equal("catalog", Assert.IsType<VectorStoreMetadata>(store.GetService(typeof(VectorStoreMetadata))).VectorStoreName);
        Assert.Equal("catalog", Assert.IsType<VectorStoreCollectionMetadata>(store.GetCollection<Guid, MilvusTestRecord>("records").GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
        Assert.Equal("catalog", Assert.IsType<VectorStoreMetadata>(provider.GetRequiredKeyedService<VectorStore>("key").GetService(typeof(VectorStoreMetadata))).VectorStoreName);
        Assert.Equal("catalog", Assert.IsType<VectorStoreCollectionMetadata>(provider.GetRequiredService<VectorStoreCollection<Guid, MilvusTestRecord>>().GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
        Assert.Equal("catalog", Assert.IsType<VectorStoreCollectionMetadata>(provider.GetRequiredKeyedService<VectorStoreCollection<Guid, MilvusTestRecord>>("key").GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
    }

    [Fact]
    public void HostRegistrationsWithoutDatabaseReportTheDefaultDatabase()
    {
        var services = new ServiceCollection()
            .AddMilvusVectorStore("localhost", options: new() { EmbeddingGenerator = new FakeEmbeddingGenerator() })
            .AddMilvusCollection<Guid, MilvusTestRecord>("records", "localhost");
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<MilvusVectorStore>();
        Assert.Equal("default", Assert.IsType<VectorStoreMetadata>(store.GetService(typeof(VectorStoreMetadata))).VectorStoreName);
        Assert.Equal("default", Assert.IsType<VectorStoreCollectionMetadata>(provider.GetRequiredService<VectorStoreCollection<Guid, MilvusTestRecord>>().GetService(typeof(VectorStoreCollectionMetadata))).VectorStoreName);
    }

    [Fact]
    public void RejectsIncompleteOrConflictingCredentials()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddMilvusVectorStore("localhost", username: "root"));
        Assert.Throws<ArgumentException>(() => services.AddKeyedMilvusVectorStore("key", "localhost", password: "password"));
        Assert.Throws<ArgumentException>(() => services.AddMilvusCollection<Guid, MilvusTestRecord>("records", "localhost", apiKey: "token", username: "root", password: "password"));
        Assert.Throws<ArgumentException>(() => services.AddKeyedMilvusCollection<Guid, MilvusTestRecord>("key", "records", "localhost", username: "root"));
    }
}
