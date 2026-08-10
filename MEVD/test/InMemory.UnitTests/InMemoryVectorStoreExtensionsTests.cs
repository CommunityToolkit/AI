// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.VectorData;
using CommunityToolkit.VectorData.InMemory;
using Xunit;

namespace InMemory.UnitTests;

public class InMemoryVectorStoreExtensionsTests
{
    [Fact]
    public async Task SerializeAndDeserializeCollectionRoundtripWorks()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var collectionName = "test-collection";
        var collection = vectorStore.GetCollection<Guid, TestRecord>(collectionName);

        var record1 = new TestRecord
        {
            Key = Guid.NewGuid(),
            Text = "First record",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f })
        };
        var record2 = new TestRecord
        {
            Key = Guid.NewGuid(),
            Text = "Second record",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.4f, 0.5f, 0.6f })
        };

        await collection.EnsureCollectionExistsAsync();
        await collection.UpsertAsync(new[] { record1, record2 });

        // Act
        using var memStream = new MemoryStream();
        await vectorStore.SerializeCollectionAsJsonAsync<Guid, TestRecord>(collectionName, memStream);
        memStream.Position = 0;

        // Simulate loading into a new store
        using var newVectorStore = new InMemoryVectorStore();
        var deserializedCollection = await newVectorStore.DeserializeCollectionFromJsonAsync<Guid, TestRecord>(memStream);

        // Assert
        Assert.NotNull(deserializedCollection);
        var loadedRecord1 = await deserializedCollection.GetAsync(record1.Key);
        var loadedRecord2 = await deserializedCollection.GetAsync(record2.Key);

        Assert.NotNull(loadedRecord1);
        Assert.NotNull(loadedRecord2);
        Assert.Equal(record1.Text, loadedRecord1.Text);
        Assert.Equal(record2.Text, loadedRecord2.Text);
        Assert.Equal(record1.Embedding, loadedRecord1.Embedding);
        Assert.Equal(record2.Embedding, loadedRecord2.Embedding);
    }

    [Fact]
    public async Task SerializeAndDeserializeCollectionRoundtripWithBuiltInEmbeddingGenerationWorks()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore(new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });
        var collectionName = "test-collection";
        var collection = vectorStore.GetCollection<Guid, TestRecordAutoEmbed>(collectionName);

        var record1 = new TestRecordAutoEmbed
        {
            Key = Guid.NewGuid(),
            Text = "First record",
        };
        var record2 = new TestRecordAutoEmbed
        {
            Key = Guid.NewGuid(),
            Text = "Second record",
        };

        await collection.EnsureCollectionExistsAsync();
        await collection.UpsertAsync(new[] { record1, record2 });

        // Act
        using var memStream = new MemoryStream();
        await vectorStore.SerializeCollectionAsJsonAsync<Guid, TestRecordAutoEmbed>(collectionName, memStream);
        memStream.Position = 0;

        // Simulate loading into a new store
        using var newVectorStore = new InMemoryVectorStore(new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });
        var deserializedCollection = await newVectorStore.DeserializeCollectionFromJsonAsync<Guid, TestRecordAutoEmbed>(memStream);

        // Assert
        Assert.NotNull(deserializedCollection);
        var loadedRecord1 = await deserializedCollection.GetAsync(record1.Key);
        var loadedRecord2 = await deserializedCollection.GetAsync(record2.Key);

        Assert.NotNull(loadedRecord1);
        Assert.NotNull(loadedRecord2);
        Assert.Equal(record1.Text, loadedRecord1.Text);
        Assert.Equal(record2.Text, loadedRecord2.Text);
    }

    [Fact]
    public async Task DeserializeCollectionFromJsonAsyncThrowsOnInvalidJson()
    {
        using var vectorStore = new InMemoryVectorStore();
        using var memStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{ invalid json }"));

        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await vectorStore.DeserializeCollectionFromJsonAsync<Guid, TestRecord>(memStream);
        });
    }

}
