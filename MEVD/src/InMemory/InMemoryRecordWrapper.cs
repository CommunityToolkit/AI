// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.InMemory;

internal readonly struct InMemoryRecordWrapper<TRecord>
{
    public InMemoryRecordWrapper(TRecord record)
    {
        Record = record;
    }

    [JsonConstructor]
    public InMemoryRecordWrapper(TRecord record, Dictionary<string, ReadOnlyMemory<float>> embeddingGeneratedVectors)
    {
        Record = record;
        EmbeddingGeneratedVectors = embeddingGeneratedVectors;
    }

    public TRecord Record { get; }
    public Dictionary<string, ReadOnlyMemory<float>> EmbeddingGeneratedVectors { get; } = [];
}
