// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
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
        // Act.
        this._serviceCollection.AddInMemoryVectorStore();

        // Assert.
        var serviceProvider = this._serviceCollection.BuildServiceProvider();
        var vectorStore = serviceProvider.GetRequiredService<VectorStore>();
        Assert.NotNull(vectorStore);
        Assert.IsType<InMemoryVectorStore>(vectorStore);
    }

    [Fact]
    public async Task AddVectorStoreAppliesConfiguredEmbeddingGenerator()
    {
        // Arrange.
        this._serviceCollection.AddInMemoryVectorStore(new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });
        var serviceProvider = this._serviceCollection.BuildServiceProvider();
        var vectorStore = serviceProvider.GetRequiredService<VectorStore>();
        var collection = vectorStore.GetCollection<string, AutoEmbedTestRecord>("testcollection");

        // Act.
        await collection.EnsureCollectionExistsAsync();
        await collection.UpsertAsync(new AutoEmbedTestRecord { Id = "1", Text = "Test record" });

        // Assert.
        var record = await collection.GetAsync("1");
        Assert.NotNull(record);
        Assert.Equal("Test record", record.Text);
    }

    [Fact]
    public void AddVectorStoreRecordCollectionRegistersClass()
    {
        // Act.
        this._serviceCollection.AddInMemoryVectorStoreRecordCollection<string, TestRecord>("testcollection");

        // Assert.
        this.AssertVectorStoreRecordCollectionCreated();
    }

    private void AssertVectorStoreRecordCollectionCreated()
    {
        var serviceProvider = this._serviceCollection.BuildServiceProvider();

        var collection = serviceProvider.GetRequiredService<VectorStoreCollection<string, TestRecord>>();
        Assert.NotNull(collection);
        Assert.IsType<InMemoryCollection<string, TestRecord>>(collection);

        var vectorizedSearch = serviceProvider.GetRequiredService<IVectorSearchable<TestRecord>>();
        Assert.NotNull(vectorizedSearch);
        Assert.IsType<InMemoryCollection<string, TestRecord>>(vectorizedSearch);
    }

#pragma warning disable CA1812 // Avoid uninstantiated internal classes
    private sealed class TestRecord
#pragma warning restore CA1812 // Avoid uninstantiated internal classes
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(dimensions: 4)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    private sealed class AutoEmbedTestRecord
    {
        [VectorStoreKey]
        public string Id { get; init; } = string.Empty;

        [VectorStoreData]
        public string Text { get; init; } = string.Empty;

        [VectorStoreVector(dimensions: 3)]
        public string Embedding => this.Text;
    }

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var value in values)
            {
                results.Add(new Embedding<float>([0.1f, 0.2f, 0.3f]));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }
}
