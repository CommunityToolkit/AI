// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

/// <summary>
/// Vector search response.
/// More information here: <see href="https://weaviate.io/developers/weaviate/api/graphql"/>.
/// </summary>
internal sealed class WeaviateVectorSearchResponse
{
    [JsonPropertyName("data")]
    public WeaviateVectorSearchData? Data { get; set; }
}
