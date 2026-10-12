// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.UnitTests;
using CommunityToolkit.VectorData.Weaviate;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace Weaviate.UnitTests;

public sealed class WeaviateHybridSearchEmbeddingGenerationTests : IDisposable
{
    private readonly HttpMessageHandlerStub _messageHandlerStub = new();
    private readonly HttpClient _httpClient;

    public WeaviateHybridSearchEmbeddingGenerationTests()
    {
        this._httpClient = new(this._messageHandlerStub, false) { BaseAddress = new Uri("http://default-endpoint") };
    }

    public void Dispose()
    {
        this._httpClient.Dispose();
        this._messageHandlerStub.Dispose();
    }

    [Fact]
    public async Task HybridSearchAsync_WithIncludeVectorsAndEmbeddingGeneration_ThrowsNotSupported()
    {
        using WeaviateCollection<Guid, Record> collection = this.CreateCollection();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await collection.HybridSearchAsync("search text", ["keyword"], top: 3, new() { IncludeVectors = true }).ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_WithIncludeVectorsAndEmbeddingGeneration_ThrowsNotSupported()
    {
        using WeaviateCollection<Guid, Record> collection = this.CreateCollection();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await collection.SearchAsync("search text", top: 3, new() { IncludeVectors = true }).ToListAsync());
    }

    private WeaviateCollection<Guid, Record> CreateCollection()
        => new(this._httpClient, "Collection", new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });

    public sealed class Record
    {
        [VectorStoreKey]
        public Guid Id { get; set; }

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Text { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public string Embedding { get; set; } = string.Empty;
    }
}
