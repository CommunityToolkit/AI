// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.VectorData;
using Moq;
using Xunit;

namespace CommunityToolkit.VectorData.Qdrant.UnitTests;

public sealed class QdrantHybridSearchEmbeddingGenerationTests
{
    [Fact]
    public async Task HybridSearchAsync_WithIncludeVectorsAndEmbeddingGeneration_ThrowsNotSupported()
    {
        using QdrantCollection<ulong, Record> collection = CreateCollection();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await collection.HybridSearchAsync("search text", ["keyword"], top: 3, new() { IncludeVectors = true }).ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_WithIncludeVectorsAndEmbeddingGeneration_ThrowsNotSupported()
    {
        using QdrantCollection<ulong, Record> collection = CreateCollection();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await collection.SearchAsync("search text", top: 3, new() { IncludeVectors = true }).ToListAsync());
    }

    private static QdrantCollection<ulong, Record> CreateCollection()
        => new(() => new Mock<MockableQdrantClient>(MockBehavior.Strict).Object, "collection", new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });

    public sealed class Record
    {
        [VectorStoreKey]
        public ulong Id { get; set; }

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Text { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public string Embedding { get; set; } = string.Empty;
    }
}
