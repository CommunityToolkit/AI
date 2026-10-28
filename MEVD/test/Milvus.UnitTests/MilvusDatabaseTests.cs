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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostRegistrationsSupportDatabaseAndBothAuthenticationModes(bool apiKey)
    {
        var services = new ServiceCollection();
        if (apiKey)
        {
            services.AddMilvusVectorStore("localhost", "token", database: "catalog")
                .AddKeyedMilvusVectorStore("key", "localhost", "token", database: "catalog")
                .AddMilvusCollection<Guid, MilvusTestRecord>("records", "localhost", "token", database: "catalog")
                .AddKeyedMilvusCollection<Guid, MilvusTestRecord>("key", "records", "localhost", "token", database: "catalog");
        }
        else
        {
            services.AddMilvusVectorStore("localhost", "root", "password", database: "catalog")
                .AddKeyedMilvusVectorStore("key", "localhost", "root", "password", database: "catalog")
                .AddMilvusCollection<Guid, MilvusTestRecord>("records", "localhost", "root", "password", database: "catalog")
                .AddKeyedMilvusCollection<Guid, MilvusTestRecord>("key", "records", "localhost", "root", "password", database: "catalog");
        }
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsEmptyCredentials(string? credential)
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<ArgumentException>(() => services.AddMilvusVectorStore("localhost", apiKey: credential!));
        Assert.ThrowsAny<ArgumentException>(() => services.AddKeyedMilvusVectorStore("key", "localhost", username: credential!, password: "password"));
        Assert.ThrowsAny<ArgumentException>(() => services.AddMilvusCollection<Guid, MilvusTestRecord>("records", "localhost", username: "root", password: credential!));
        Assert.ThrowsAny<ArgumentException>(() => services.AddKeyedMilvusCollection<Guid, MilvusTestRecord>("key", "records", "localhost", apiKey: credential!));
    }
}
