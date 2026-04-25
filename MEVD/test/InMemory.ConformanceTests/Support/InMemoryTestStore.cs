// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.InMemory;
using VectorData.ConformanceTests.Support;

namespace InMemory.ConformanceTests.Support;

internal sealed class InMemoryTestStore : TestStore
{
    public static InMemoryTestStore Instance { get; } = new();

    public InMemoryVectorStore GetVectorStore(InMemoryVectorStoreOptions options)
        => new(new() { EmbeddingGenerator = options.EmbeddingGenerator });

    private InMemoryTestStore()
    {
    }

    protected override Task StartAsync()
    {
        this.DefaultVectorStore = new InMemoryVectorStore();

        return Task.CompletedTask;
    }
}
