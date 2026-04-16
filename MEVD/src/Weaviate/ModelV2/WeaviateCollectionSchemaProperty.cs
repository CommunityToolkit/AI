// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateCollectionSchemaProperty
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("dataType")]
    public List<string> DataType { get; set; } = [];

    [JsonPropertyName("indexFilterable")]
    public bool IndexFilterable { get; set; }

    [JsonPropertyName("indexSearchable")]
    public bool IndexSearchable { get; set; }
}
