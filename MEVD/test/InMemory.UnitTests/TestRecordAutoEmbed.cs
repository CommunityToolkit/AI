// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.VectorData;

namespace InMemory.UnitTests;

internal sealed class TestRecordAutoEmbed
{
    [VectorStoreKey]
    public Guid Key { get; init; }

    [VectorStoreData]
    public string Text { get; init; } = string.Empty;

    [VectorStoreVector(dimensions: 3)]
    public string Embedding => this.Text;
}
