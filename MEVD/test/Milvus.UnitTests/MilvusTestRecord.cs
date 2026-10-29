// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusTestRecord
{
    [VectorStoreKey(StorageName = "record_id")]
    public Guid Key { get; set; }

    [VectorStoreData(StorageName = "label")]
    public string? Name { get; set; }

    [VectorStoreData]
    public int? Count { get; set; }

    [VectorStoreData]
    public bool Enabled { get; set; }

    [VectorStoreVector(3, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
