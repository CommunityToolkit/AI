// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq.Expressions;
using CommunityToolkit.VectorData.Milvus;
using Microsoft.Extensions.VectorData;
using Milvus.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests;

public sealed class MilvusArrayFilterTests : IClassFixture<MilvusFixture>
{
    [Fact]
    public async Task ArrayMembershipPreservesNullSemanticsForRetrievalAndSearch()
    {
        using var collection = new MilvusCollection<string, Record>(MilvusTestStore.Instance.Client,
            "ct_arrays_" + Guid.NewGuid().ToString("N"), ownsClient: false);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new[]
            {
                new Record { Id = "null" },
                new Record { Id = "empty", Tags = [], ListTags = [] },
                new Record { Id = "other", Tags = ["y"], ListTags = ["y"] },
                new Record { Id = "match", Tags = ["x"], ListTags = ["x"] },
            });
            var candidates = new[] { "x", null };
            var nulls = new string?[] { null };
            var empty = Array.Empty<string>();
            (Expression<Func<Record, bool>> Filter, string[] Expected)[] cases =
            [
                (r => r.Tags!.Contains("x"), ["match"]),
                (r => !r.Tags!.Contains("x"), ["null", "empty", "other"]),
                (r => r.ListTags!.Contains("x"), ["match"]),
                (r => !r.ListTags!.Contains("x"), ["null", "empty", "other"]),
                (r => r.Tags!.Any(t => new[] { "x", null }.Contains(t)), ["match"]),
                (r => !r.Tags!.Any(t => candidates.Contains(t)), ["null", "empty", "other"]),
                (r => r.ListTags!.Any(t => candidates.Contains(t)), ["match"]),
                (r => !r.ListTags!.Any(t => candidates.Contains(t)), ["null", "empty", "other"]),
                (r => r.Tags!.Any(t => nulls.Contains(t)), []),
                (r => !r.Tags!.Any(t => nulls.Contains(t)), ["null", "empty", "other", "match"]),
                (r => r.Tags!.Any(t => empty.Contains(t)), []),
                (r => r.Tags!.Contains(null!), []),
                (r => !(r.Tags!.Contains("x") && r.ListTags!.Contains("x")), ["null", "empty", "other"]),
                (r => !(r.Tags!.Contains("x") || r.ListTags!.Contains("x")), ["null", "empty", "other"]),
                (r => !!r.Tags!.Contains("x"), ["match"]),
            ];
            foreach (var (filter, expected) in cases)
            {
                var retrieved = await collection.GetAsync(filter, 10).ToListAsync();
                Assert.Equal(expected.OrderBy(id => id), retrieved.Select(r => r.Id).OrderBy(id => id));
                var searched = await collection.SearchAsync(new float[] { 1, 0, 0 }, 10,
                    new() { Filter = filter }).ToListAsync();
                Assert.Equal(expected.OrderBy(id => id), searched.Select(r => r.Record.Id).OrderBy(id => id));
            }
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

        [VectorStoreData(StorageName = "tags")]
        public string[]? Tags { get; set; }

        [VectorStoreData]
        public List<string>? ListTags { get; set; }

        [VectorStoreVector(3, IndexKind = IndexKind.Flat)]
        public ReadOnlyMemory<float> Vector { get; set; } = new float[] { 1, 0, 0 };
    }
}
