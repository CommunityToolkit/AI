// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateQueryMatchWhereClause
{
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("path")]
    public List<string> Path { get; set; } = [];

    [JsonPropertyName("valueTextArray")]
    public List<string> Values { get; set; } = [];
}
