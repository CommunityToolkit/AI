// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

/// <summary>
/// Vector search data model.
/// More information here: <see href="https://weaviate.io/developers/weaviate/api/graphql"/>.
/// </summary>
internal sealed class WeaviateVectorSearchData
{
    [JsonPropertyName("Get")]
    public Dictionary<string, JsonArray>? GetOperation { get; set; }
}
