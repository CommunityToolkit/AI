// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Linq.Expressions;
using CommunityToolkit.VectorData.Milvus;
using Microsoft.Extensions.VectorData;
using Milvus.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests;

public sealed class MilvusNullComparisonTests : IClassFixture<MilvusFixture>
{
    [Fact]
    public async Task NullComparisonsMatchCSharpForRetrievalAndSearch()
    {
        using var collection = new MilvusCollection<string, MilvusRoundTripTests.Record>(MilvusTestStore.Instance.Client,
            "ct_null_" + Guid.NewGuid().ToString("N"), ownsClient: false);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new[]
            {
                new MilvusRoundTripTests.Record { Id = "null", Count = null, Vector = new float[] { 1, 0, 0 } },
                new MilvusRoundTripTests.Record { Id = "value", Count = 1, Vector = new float[] { 1, 0, 0 } },
            });
            int? value = null;
            Assert.Empty(await collection.GetAsync(r => r.Count > value, 10).ToListAsync());
            Assert.Empty(await collection.GetAsync(r => r.Count >= value, 10).ToListAsync());
            Assert.Empty(await collection.GetAsync(r => value < r.Count, 10).ToListAsync());
            Assert.Empty(await collection.GetAsync(r => value <= r.Count, 10).ToListAsync());
            Assert.Equal(2, (await collection.GetAsync(r => !(r.Count > value), 10).ToListAsync()).Count);
            Assert.Empty(await collection.SearchAsync(new float[] { 1, 0, 0 }, 10,
                new() { Filter = r => r.Count > value }).ToListAsync());
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    [Fact]
    public async Task ComparisonsOfTwoPropertiesMatchCSharpWithEveryCombinationOfNulls()
    {
        using var collection = new MilvusCollection<string, Pair>(MilvusTestStore.Instance.Client,
            "ct_null_pairs_" + Guid.NewGuid().ToString("N"), ownsClient: false);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            int?[] numbers = [null, 1, 2];
            string?[] texts = [null, "x", "y"];
            var records = (from a in Enumerable.Range(0, 3)
                           from b in Enumerable.Range(0, 3)
                           select new Pair
                           {
                               Id = $"{a}{b}",
                               A = numbers[a],
                               B = numbers[b],
                               I = b,
                               S = texts[a],
                               T = texts[b],
                               Vector = new float[] { 1, 0 },
                           }).ToList();
            await collection.UpsertAsync(records);
            Expression<Func<Pair, bool>>[] filters =
            [
                r => r.A == r.B, r => r.A != r.B, r => r.A < r.B, r => r.A <= r.B, r => r.A > r.B, r => r.A >= r.B,
                r => !(r.A == r.B), r => !(r.A != r.B), r => !(r.A < r.B), r => !(r.A <= r.B), r => !(r.A > r.B), r => !(r.A >= r.B),
                r => r.S == r.T, r => r.S != r.T, r => !(r.S == r.T), r => !(r.S != r.T),
                r => r.A == r.I, r => r.I != r.A, r => r.I < r.A, r => !(r.A == r.I), r => !(r.I < r.A),
                r => r.A == r.B || r.S == r.T, r => !(r.A == r.B && r.S != r.T),
            ];

            foreach (var filter in filters)
            {
                var expected = records.Where(filter.Compile()).Select(r => r.Id).Order(StringComparer.Ordinal).ToList();
                var retrieved = (await collection.GetAsync(filter, 100).ToListAsync()).Select(r => r.Id).Order(StringComparer.Ordinal).ToList();
                var found = (await collection.SearchAsync(new float[] { 1, 0 }, 100, new() { Filter = filter }).ToListAsync())
                    .Select(r => r.Record.Id).Order(StringComparer.Ordinal).ToList();
                Assert.True(expected.SequenceEqual(retrieved), $"{filter}: expected [{string.Join(", ", expected)}], retrieved [{string.Join(", ", retrieved)}]");
                Assert.True(expected.SequenceEqual(found), $"{filter}: expected [{string.Join(", ", expected)}], found [{string.Join(", ", found)}]");
            }
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    public sealed class Pair
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public int? A { get; set; }

        [VectorStoreData]
        public int? B { get; set; }

        [VectorStoreData]
        public int I { get; set; }

        [VectorStoreData]
        public string? S { get; set; }

        [VectorStoreData]
        public string? T { get; set; }

        [VectorStoreVector(2, IndexKind = IndexKind.Flat)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }
}
