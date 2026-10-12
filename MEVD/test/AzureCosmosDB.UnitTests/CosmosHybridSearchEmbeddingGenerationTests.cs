// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.VectorData;
using Moq;
using Xunit;

namespace CommunityToolkit.VectorData.AzureCosmosDB.UnitTests;

public sealed class CosmosHybridSearchEmbeddingGenerationTests
{
    [Fact]
    public async Task HybridSearchAsync_WithStringInputAndEmbeddingGenerator_GeneratesEmbeddingAndQueries()
    {
        FakeEmbeddingGenerator generator = new();
        QueryDefinition? capturedQuery = null;
        CosmosCollection<string, Record> collection = CreateCollection(generator, query => capturedQuery = query);

        List<VectorSearchResult<Record>> results = await collection
            .HybridSearchAsync("search text", ["keyword"], top: 3)
            .ToListAsync();

        Assert.Empty(results);
        Assert.Equal(["search text"], generator.Inputs);
        Assert.NotNull(capturedQuery);
        Assert.Contains("FullTextScore", capturedQuery.QueryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HybridSearchAsync_WithStringInputAndNoEmbeddingGenerator_ThrowsNotSupported()
    {
        CosmosCollection<string, RecordWithNativeVector> collection = CreateCollection<RecordWithNativeVector>(generator: null, _ => { });

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await collection.HybridSearchAsync("search text", ["keyword"], top: 3).ToListAsync());
    }

    private static CosmosCollection<string, TRecord> CreateCollection<TRecord>(
        IEmbeddingGenerator? generator,
        Action<QueryDefinition> onQuery)
        where TRecord : class
    {
        Mock<FeedIterator<JsonObject>> feedIterator = new();
        feedIterator.SetupGet(i => i.HasMoreResults).Returns(false);

        Mock<Container> container = new();
        container
            .Setup(c => c.GetItemQueryIterator<JsonObject>(It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Callback((QueryDefinition query, string? _, QueryRequestOptions? _) => onQuery(query))
            .Returns(feedIterator.Object);

        Mock<CosmosClient> client = new();
        client.SetupGet(c => c.ClientOptions).Returns(new CosmosClientOptions { UseSystemTextJsonSerializerWithOptions = new() });
        Mock<Database> database = new();
        database.SetupGet(d => d.Client).Returns(client.Object);
        database.Setup(d => d.GetContainer(It.IsAny<string>())).Returns(container.Object);

        return new CosmosCollection<string, TRecord>(
            database.Object,
            "collection",
            new() { EmbeddingGenerator = generator });
    }

    private static CosmosCollection<string, Record> CreateCollection(FakeEmbeddingGenerator generator, Action<QueryDefinition> onQuery)
        => CreateCollection<Record>(generator, onQuery);

    public sealed class Record
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Text { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public string Embedding { get; set; } = string.Empty;
    }

    public sealed class RecordWithNativeVector
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Text { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }
}
