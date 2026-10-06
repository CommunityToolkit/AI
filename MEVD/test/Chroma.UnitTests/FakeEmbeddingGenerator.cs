// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Chroma.UnitTests;

/// <summary>
/// An embedding generator that turns each text into the vector [1, 2, 3, 4], and keeps the texts it was given.
/// </summary>
internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<string> Texts { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var texts = values.ToList();
        this.Texts.AddRange(texts);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(texts.Select(_ => new Embedding<float>(new float[] { 1, 2, 3, 4 }))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
