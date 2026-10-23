// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusHybridSearchTests
{
    private static readonly VectorStoreCollectionDefinition s_definition = new()
    {
        Properties =
        [
            new VectorStoreKeyProperty("Id", typeof(string)),
            new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true },
            new VectorStoreVectorProperty("Embedding", typeof(string), 3),
        ],
    };

    [Fact]
    public void GetServiceReturnsTheCollectionWhenAPropertyIsFullTextIndexed()
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var typed = new MilvusCollection<string, TextRecord>(client, "typed", ownsClient: false);
        using var dynamic = new MilvusDynamicCollection(client, "dynamic", ownsClient: false, new() { Definition = s_definition, EmbeddingGenerator = new FakeEmbeddingGenerator() });
        using var withoutText = new MilvusCollection<Guid, MilvusTestRecord>(client, "without_text", ownsClient: false);

        Assert.Same(typed, typed.GetService(typeof(IKeywordHybridSearchable<TextRecord>)));
        Assert.Same(dynamic, dynamic.GetService(typeof(IKeywordHybridSearchable<Dictionary<string, object?>>)));
        Assert.Null(withoutText.GetService(typeof(IKeywordHybridSearchable<MilvusTestRecord>)));
        Assert.Null(typed.GetService(typeof(IKeywordHybridSearchable<TextRecord>), "key"));
    }

    [Fact]
    public void DependencyInjectionResolvesHybridSearch()
    {
        using var client = new MilvusClient("localhost");
        using var provider = new ServiceCollection().AddSingleton(client)
            .AddMilvusCollection<string, TextRecord>("records")
            .AddKeyedMilvusCollection<string, TextRecord>("key", "other_records")
            .BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<VectorStoreCollection<string, TextRecord>>(), provider.GetRequiredService<IKeywordHybridSearchable<TextRecord>>());
        Assert.Same(provider.GetRequiredKeyedService<VectorStoreCollection<string, TextRecord>>("key"), provider.GetRequiredKeyedService<IKeywordHybridSearchable<TextRecord>>("key"));
    }

    [Fact]
    public async Task RejectsInvalidHybridSearchesBeforeContactingTheServer()
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var collection = new MilvusCollection<string, TextRecord>(client, "records", ownsClient: false);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await collection.HybridSearchAsync(new float[] { 1, 2, 3 }, ["apples"], 0).ToListAsync());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await collection.HybridSearchAsync(new float[] { 1, 2, 3 }, null!, 1).ToListAsync());
        await Assert.ThrowsAsync<ArgumentException>(async () => await collection.HybridSearchAsync(new float[] { 1 }, ["apples"], 1).ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await collection.HybridSearchAsync(new float[] { 1, 2, 3 }, ["apples"], 1,
            new() { AdditionalProperty = r => r.Title }).ToListAsync());
    }

    [Theory]
    [InlineData(16384, 1)]
    [InlineData(1, 16384)]
    [InlineData(int.MaxValue, 1)]
    public async Task RejectsTopAndSkipBeyondTheMilvusLimitBeforeContactingTheServer(int top, int skip)
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var collection = new MilvusCollection<string, TextRecord>(client, "records", ownsClient: false);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await collection.HybridSearchAsync(new float[] { 1, 2, 3 }, ["apples"], top,
            new() { Skip = skip }).ToListAsync());
        Assert.Equal("top", exception.ParamName);
        Assert.Contains("16384", exception.Message);
    }

    [Fact]
    public async Task RejectsIncludeVectorsWithEmbeddingGeneration()
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var collection = new MilvusDynamicCollection(client, "records", ownsClient: false, new() { Definition = s_definition, EmbeddingGenerator = new FakeEmbeddingGenerator() });

        await Assert.ThrowsAsync<NotSupportedException>(async () => await collection.HybridSearchAsync("apples", ["apples"], 1, new() { IncludeVectors = true }).ToListAsync());
    }

    [Fact]
    public async Task RejectsHybridSearchWithoutAFullTextProperty()
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var collection = new MilvusCollection<Guid, MilvusTestRecord>(client, "records", ownsClient: false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await collection.HybridSearchAsync(new float[] { 1, 2, 3 }, ["apples"], 1).ToListAsync());
    }

    public sealed class TextRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public string? Text { get; set; }

        [VectorStoreData]
        public string? Title { get; set; }

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }
}
