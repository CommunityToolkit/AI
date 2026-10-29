// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.Milvus;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Xunit;
using Milvus.ConformanceTests.Support;

namespace Milvus.ConformanceTests;

public sealed class MilvusRoundTripTests : IClassFixture<MilvusFixture>
{
    private readonly MilvusClient _client;

    public MilvusRoundTripTests(MilvusFixture fixture) => _client = ((MilvusTestStore)fixture.TestStore).Client;

    [Fact]
    public async Task TypedCrudFilteringAndSearch()
    {
        var client = _client;
        using var collection = new MilvusCollection<string, Record>(client, "ct_" + Guid.NewGuid().ToString("N"), ownsClient: false);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            Assert.True(await collection.CollectionExistsAsync());
            await collection.UpsertAsync(new Record { Id = "a", Label = "first", Count = null, Vector = new float[] { 1, 0, 0 } });
            await collection.UpsertAsync(new Record { Id = "b", Label = "second", Count = 2, Vector = new float[] { 0, 1, 0 } });
            var first = await collection.GetAsync("a", new() { IncludeVectors = true });
            Assert.NotNull(first);
            Assert.Null(first.Count);
            Assert.Equal("first", first.Label);
            Assert.Equal(new float[] { 1, 0, 0 }, first.Vector.ToArray());
            var found = await collection.SearchAsync(new float[] { 1, 0, 0 }, 1).ToListAsync();
            Assert.Equal("a", Assert.Single(found).Record.Id);
            Assert.Equal(2, (await collection.GetAsync(r => true, 10).ToListAsync()).Count);
            Assert.Empty(await collection.GetAsync(r => false, 10).ToListAsync());
            var noIds = Array.Empty<string>();
            Assert.Empty(await collection.GetAsync(r => noIds.Contains(r.Id), 10).ToListAsync());
            var filtered = await collection.GetAsync(r => r.Count == 2, 10).ToListAsync();
            Assert.Equal("b", Assert.Single(filtered).Id);
            await collection.UpsertAsync(new Record { Id = "a", Label = "updated", Count = 3, Vector = new float[] { 1, 0, 0 } });
            Assert.Equal("updated", (await collection.GetAsync("a"))!.Label);
            await collection.DeleteAsync("a");
            Assert.Null(await collection.GetAsync("a"));
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    [Fact]
    public async Task DynamicMultipleVectorsAndLongKeys()
    {
        var client = _client;
        using var store = new MilvusVectorStore(client, ownsClient: false);
        using var collection = store.GetDynamicCollection("ct_" + Guid.NewGuid().ToString("N"), new()
        {
            Properties = [new VectorStoreKeyProperty("Id", typeof(long)),
                new VectorStoreDataProperty("Label", typeof(string)),
                new VectorStoreVectorProperty("First", typeof(float[]), 3) { IndexKind = IndexKind.Flat },
                new VectorStoreVectorProperty("Second", typeof(float[]), 3) { IndexKind = IndexKind.Flat }],
        });
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new Dictionary<string, object?>
            {
                ["Id"] = 42L, ["Label"] = "two vectors", ["First"] = new float[] { 1, 0, 0 }, ["Second"] = new float[] { 0, 1, 0 },
            });
            var row = await collection.GetAsync(42L, new() { IncludeVectors = true });
            Assert.NotNull(row);
            Assert.Equal(42L, row["Id"]);
            Assert.Equal(new float[] { 1, 0, 0 }, Assert.IsType<float[]>(row["First"]));
            Assert.Equal(new float[] { 0, 1, 0 }, Assert.IsType<float[]>(row["Second"]));
            var results = await collection.SearchAsync(new float[] { 0, 1, 0 }, 1,
                new() { VectorProperty = r => r["Second"] }).ToListAsync();
            Assert.Equal(42L, Assert.Single(results).Record["Id"]);
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchReturnsNoResultsForUnmatchedFiltersAndDeletedRecords(bool includeVectors)
    {
        var client = _client;
        using var collection = new MilvusCollection<string, Record>(client, "ct_" + Guid.NewGuid().ToString("N"), ownsClient: false);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new Record { Id = "one", Count = 1, Vector = new float[] { 1, 0, 0 } });
            Assert.Empty(await collection.SearchAsync(new float[] { 1, 0, 0 }, 5,
                new() { Filter = r => r.Count == 99, IncludeVectors = includeVectors }).ToListAsync());
            await collection.DeleteAsync("one");
            Assert.Empty(await collection.SearchAsync(new float[] { 1, 0, 0 }, 5,
                new() { IncludeVectors = includeVectors }).ToListAsync());
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    public sealed class Record
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public string? Label { get; set; }

        [VectorStoreData]
        public int? Count { get; set; }

        [VectorStoreVector(3, IndexKind = IndexKind.Flat)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }
}
