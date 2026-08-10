// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using CommunityToolkit.VectorData.InMemory;
using Xunit;

namespace InMemory.UnitTests;

/// <summary>
/// Contains tests for the <see cref="InMemoryServiceCollectionExtensions"/> class.
/// </summary>
public class InMemoryServiceCollectionExtensionsTests
{
    private readonly IServiceCollection _serviceCollection;

    public InMemoryServiceCollectionExtensionsTests()
    {
        this._serviceCollection = new ServiceCollection();
    }

    [Fact]
    public void AddVectorStoreRegistersClass()
    {
        this._serviceCollection.AddInMemoryVectorStore();

        ServiceProvider serviceProvider = this._serviceCollection.BuildServiceProvider();
        VectorStore vectorStore = serviceProvider.GetRequiredService<VectorStore>();
        Assert.NotNull(vectorStore);
        Assert.IsType<InMemoryVectorStore>(vectorStore);
    }

    [Fact]
    public async Task AddVectorStoreAppliesConfiguredEmbeddingGenerator()
    {
        FakeEmbeddingGenerator embeddingGenerator = new();
        this._serviceCollection.AddInMemoryVectorStore(new() { EmbeddingGenerator = embeddingGenerator });
        ServiceProvider serviceProvider = this._serviceCollection.BuildServiceProvider();
        VectorStore vectorStore = serviceProvider.GetRequiredService<VectorStore>();
        VectorStoreCollection<Guid, TestRecordAutoEmbed> collection = vectorStore.GetCollection<Guid, TestRecordAutoEmbed>("testcollection");
        Guid key = Guid.NewGuid();

        await collection.EnsureCollectionExistsAsync();
        await collection.UpsertAsync(new TestRecordAutoEmbed { Key = key, Text = "Test record" });

        TestRecordAutoEmbed? record = await collection.GetAsync(key);
        Assert.NotNull(record);
        Assert.Equal("Test record", record.Text);
        Assert.Equal(1, embeddingGenerator.CallCount);
    }

    [Fact]
    public void AddVectorStoreRecordCollectionRegistersClass()
    {
        this._serviceCollection.AddInMemoryVectorStoreRecordCollection<Guid, TestRecord>("testcollection");

        this.AssertVectorStoreRecordCollectionCreated();
    }

    private void AssertVectorStoreRecordCollectionCreated()
    {
        ServiceProvider serviceProvider = this._serviceCollection.BuildServiceProvider();

        VectorStoreCollection<Guid, TestRecord> collection = serviceProvider.GetRequiredService<VectorStoreCollection<Guid, TestRecord>>();
        Assert.NotNull(collection);
        Assert.IsType<InMemoryCollection<Guid, TestRecord>>(collection);

        IVectorSearchable<TestRecord> vectorizedSearch = serviceProvider.GetRequiredService<IVectorSearchable<TestRecord>>();
        Assert.NotNull(vectorizedSearch);
        Assert.IsType<InMemoryCollection<Guid, TestRecord>>(vectorizedSearch);
    }
}
