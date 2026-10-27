// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests;

public class MilvusHybridSearchTests(MilvusHybridSearchTests.VectorAndStringFixture single, MilvusHybridSearchTests.MultiTextFixture multi)
    : HybridSearchTests<string>(single, multi), IClassFixture<MilvusHybridSearchTests.VectorAndStringFixture>, IClassFixture<MilvusHybridSearchTests.MultiTextFixture>
{
    private static readonly ReadOnlyMemory<float> s_vector = new float[] { 1, 0, 0, 0 };

    [Fact]
    public async Task HybridSearchAsync_with_a_filter_matching_no_record_returns_no_record()
        => Assert.Empty(await single.HybridSearchable.HybridSearchAsync(s_vector, ["Oranges"], 3, new() { Filter = r => r.Code == 99 }).ToListAsync());

    [Fact]
    public async Task HybridSearchAsync_with_ScoreThreshold_keeps_the_scores_at_or_above_it()
    {
        // Only the record with the keyword ranks in both searches: its RRF score is well above the others, whatever the
        // order of the records with the same vector.
        var all = await single.HybridSearchable.HybridSearchAsync(s_vector, ["Grapes"], 3).ToListAsync();
        var threshold = (all[0].Score!.Value + all[1].Score!.Value) / 2;

        var results = await single.HybridSearchable.HybridSearchAsync(s_vector, ["Grapes"], 3, new() { ScoreThreshold = threshold }).ToListAsync();

        Assert.Equal(3, Assert.Single(results).Record.Code);
    }

    [Fact]
    public async Task HybridSearchAsync_with_IncludeVectors_returns_the_vectors()
    {
        var withVectors = await single.HybridSearchable.HybridSearchAsync(s_vector, ["Grapes"], 1, new() { IncludeVectors = true }).ToListAsync();
        var withoutVectors = await single.HybridSearchable.HybridSearchAsync(s_vector, ["Grapes"], 1).ToListAsync();

        Assert.Equal(s_vector.ToArray(), Assert.Single(withVectors).Record.Vector.ToArray());
        Assert.True(Assert.Single(withoutVectors).Record.Vector.IsEmpty);
    }

    [Fact]
    public async Task HybridSearchAsync_with_top_and_Skip_at_the_Milvus_limit()
        => Assert.Equal(2, (await single.HybridSearchable.HybridSearchAsync(s_vector, ["Grapes"], 16383, new() { Skip = 1 }).ToListAsync()).Count);

    [Fact]
    public async Task HybridSearchAsync_without_keywords_returns_the_vector_ranking()
    {
        // Distinct vectors, so that the vector ranking is near, middle, far; the keyword would rank far and middle first.
        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("Key", typeof(string)),
                new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreVectorProperty("Vector", typeof(ReadOnlyMemory<float>), 4) { IndexKind = IndexKind.Flat },
            ],
        };
        using var store = MilvusTestStore.Instance.GetVectorStore(new());
        using var collection = store.GetDynamicCollection("ct_hybrid_no_keywords_" + Guid.NewGuid().ToString("N"), definition);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(
            [
                new() { ["Key"] = "near", ["Text"] = "Pears", ["Vector"] = new ReadOnlyMemory<float>([1, 0, 0, 0]) },
                new() { ["Key"] = "middle", ["Text"] = "Apples and apples", ["Vector"] = new ReadOnlyMemory<float>([1, 1, 0, 0]) },
                new() { ["Key"] = "far", ["Text"] = "Apples", ["Vector"] = new ReadOnlyMemory<float>([0, 0, 1, 0]) },
            ]);
            var hybrid = (IKeywordHybridSearchable<Dictionary<string, object?>>)collection.GetService(typeof(IKeywordHybridSearchable<Dictionary<string, object?>>))!;

            var vectorRanking = await collection.SearchAsync(s_vector, 3).Select(r => (string)r.Record["Key"]!).ToListAsync();
            var withoutKeywords = await hybrid.HybridSearchAsync(s_vector, [], 3).ToListAsync();
            var withKeyword = await hybrid.HybridSearchAsync(s_vector, ["Apples"], 3).Select(r => (string)r.Record["Key"]!).ToListAsync();

            Assert.Equal(["near", "middle", "far"], vectorRanking);
            Assert.Equal(vectorRanking, withoutKeywords.Select(r => (string)r.Record["Key"]!));
            for (var rank = 0; rank < 3; rank++)
            {
                // The RRF score of the vector ranking alone, with k = 60.
                Assert.Equal(1d / (61 + rank), withoutKeywords[rank].Score!.Value, 1e-6);
            }
            Assert.Equal("near", withKeyword[2]);
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    public new class VectorAndStringFixture : HybridSearchTests<string>.VectorAndStringFixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
    public new class MultiTextFixture : HybridSearchTests<string>.MultiTextFixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}
