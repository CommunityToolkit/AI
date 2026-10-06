// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.VectorData.Chroma;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Chroma.UnitTests;

/// <summary>
/// Builds the model of a test record, as the collection does.
/// </summary>
internal static class ChromaTestModel
{
    public static CollectionModel Build<TRecord>(IEmbeddingGenerator? embeddingGenerator = null)
        => new ChromaModelBuilder().Build(
            typeof(TRecord),
            typeof(TRecord).GetProperties().Single(property => property.IsDefined(typeof(VectorStoreKeyAttribute), inherit: false)).PropertyType,
            definition: null,
            embeddingGenerator);
}

public sealed class ChromaHotel<TKey>
{
    [VectorStoreKey]
    public TKey HotelId { get; set; } = default!;

    [VectorStoreData]
    public string? HotelName { get; set; }

    [VectorStoreData]
    public int? Rating { get; set; }

    [VectorStoreData]
    public double Price { get; set; }

    [VectorStoreData(StorageName = "parking_is_included")]
    public bool Parking { get; set; }

    [VectorStoreData]
    public List<string>? Tags { get; set; }

    [VectorStoreVector(4)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

public sealed class DatedHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreData]
    public DateTimeOffset Opened { get; set; }

    [VectorStoreData]
    public DateTime Updated { get; set; }

    [VectorStoreData]
    public List<DateTimeOffset>? Visits { get; set; }

    [VectorStoreVector(4)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

public sealed class DotProductHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreVector(4, DistanceFunction = DistanceFunction.DotProductSimilarity)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

public sealed class TwoFullTextHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreData(IsFullTextIndexed = true)]
    public string? Description { get; set; }

    [VectorStoreData(IsFullTextIndexed = true)]
    public string? Review { get; set; }

    [VectorStoreVector(4)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

public sealed class FullTextHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreData(IsFullTextIndexed = true)]
    public string? Description { get; set; }

    [VectorStoreData]
    public int Rating { get; set; }

    [VectorStoreVector(4)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

public sealed class NumberHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreData]
    public long Visits { get; set; }

    [VectorStoreData]
    public float Stars { get; set; }

    [VectorStoreVector(4)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

public sealed class EmbeddingHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreVector(4)]
    public Embedding<float>? Embedding { get; set; }
}

public sealed class ArrayHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreVector(4)]
    public float[]? Embedding { get; set; }
}

public sealed class TextHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreVector(4)]
    public string? Embedding { get; set; }
}

public sealed class FullTextTextHotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = default!;

    [VectorStoreData(IsFullTextIndexed = true)]
    public string? Description { get; set; }

    [VectorStoreVector(4)]
    public string? Embedding { get; set; }
}
