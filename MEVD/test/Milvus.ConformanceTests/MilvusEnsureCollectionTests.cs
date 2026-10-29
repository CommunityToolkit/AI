// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.Milvus;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Milvus.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests;

public sealed class MilvusEnsureCollectionTests : IClassFixture<MilvusFixture>
{
    [Fact]
    public async Task EnsureCollectionExistsKeepsTheIndexOfAnExistingCollection()
    {
        // The collection exists with an HNSW index, where the model asks for a Flat one: as another tool could create it.
        var client = MilvusTestStore.Instance.Client;
        var name = "ct_existing_index_" + Guid.NewGuid().ToString("N");
        await client.CreateCollectionAsync(name,
            [FieldSchema.CreateVarchar("Id", 65535, isPrimaryKey: true), FieldSchema.CreateVarchar("Text", 65535, nullable: true), FieldSchema.CreateFloatVector("Vector", 3)]);
        var records = client.GetCollection(name);
        await records.CreateIndexAsync("Vector", IndexType.Hnsw, SimilarityMetricType.Cosine,
            extraParams: new Dictionary<string, string> { ["M"] = "8", ["efConstruction"] = "64" });
        using var collection = new MilvusCollection<string, Record>(client, name, ownsClient: false);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new Record { Id = "a", Text = "apples", Vector = new float[] { 1, 0, 0 } });

            Assert.Equal("a", Assert.Single(await collection.SearchAsync(new float[] { 1, 0, 0 }, 1).ToListAsync()).Record.Id);
            Assert.Contains(Assert.Single(await records.DescribeIndexAsync("Vector")).Params, p => p.Value == "HNSW");
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    [Fact]
    public async Task EnsureCollectionExistsAcceptsAnExistingCollectionWithoutTheBm25Field()
    {
        // The collection was created before the text property was full-text indexed: it has no BM25 field.
        var client = MilvusTestStore.Instance.Client;
        var name = "ct_existing_text_" + Guid.NewGuid().ToString("N");
        using (var before = new MilvusCollection<string, Record>(client, name, ownsClient: false))
        {
            await before.EnsureCollectionExistsAsync();
        }
        using var collection = new MilvusCollection<string, FullTextRecord>(client, name, ownsClient: false);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new FullTextRecord { Id = "a", Text = "apples", Vector = new float[] { 1, 0, 0 } });

            Assert.Equal("a", Assert.Single(await collection.SearchAsync(new float[] { 1, 0, 0 }, 1).ToListAsync()).Record.Id);
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
        public string? Text { get; set; }

        [VectorStoreVector(3, IndexKind = IndexKind.Flat)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    public sealed class FullTextRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public string? Text { get; set; }

        [VectorStoreVector(3, IndexKind = IndexKind.Flat)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }
}
