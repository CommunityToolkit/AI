// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace InMemory.UnitTests;

internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public int CallCount { get; private set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.CallCount++;
        GeneratedEmbeddings<Embedding<float>> results = new();

        foreach (string value in values)
        {
            results.Add(new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }));
        }

        return Task.FromResult(results);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => null;

    public void Dispose()
    {
    }
}
