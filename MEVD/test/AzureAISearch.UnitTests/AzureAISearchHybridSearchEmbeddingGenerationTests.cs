// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Search.Documents.Indexes;
using CommunityToolkit.VectorData.AzureAISearch;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.VectorData;
using Moq;
using Xunit;

namespace AzureAISearch.UnitTests;

public sealed class AzureAISearchHybridSearchEmbeddingGenerationTests
{
    [Fact]
    public async Task HybridSearchAsync_WithIncludeVectorsAndEmbeddingGeneration_ThrowsNotSupported()
    {
        using AzureAISearchCollection<string, Record> collection = CreateCollection();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await collection.HybridSearchAsync("search text", ["keyword"], top: 3, new() { IncludeVectors = true }).ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_WithIncludeVectorsAndEmbeddingGeneration_ThrowsNotSupported()
    {
        using AzureAISearchCollection<string, Record> collection = CreateCollection();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await collection.SearchAsync("search text", top: 3, new() { IncludeVectors = true }).ToListAsync());
    }

    private static AzureAISearchCollection<string, Record> CreateCollection()
        => new(new Mock<SearchIndexClient>().Object, "collection", new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });

    public sealed class Record
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Text { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public string Embedding { get; set; } = string.Empty;
    }
}
