// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateQueryMatch
{
    [JsonPropertyName("class")]
    public string? CollectionName { get; set; }

    [JsonPropertyName("where")]
    public WeaviateQueryMatchWhereClause? WhereClause { get; set; }
}
