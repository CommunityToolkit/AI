// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateCollectionSchema
{
    [JsonConstructor]
    public WeaviateCollectionSchema(string collectionName)
    {
        CollectionName = collectionName;
    }

    [JsonPropertyName("class")]
    public string CollectionName { get; set; }

    [JsonPropertyName("vectorConfig")]
    public Dictionary<string, WeaviateCollectionSchemaVectorConfig> VectorConfigurations { get; set; } = [];

    [JsonPropertyName("properties")]
    public List<WeaviateCollectionSchemaProperty> Properties { get; set; } = [];

    [JsonPropertyName("vectorizer")]
    public string Vectorizer { get; set; } = WeaviateConstants.DefaultVectorizer;

    [JsonPropertyName("vectorIndexType")]
    public string? VectorIndexType { get; set; }

    [JsonPropertyName("vectorIndexConfig")]
    public WeaviateCollectionSchemaVectorIndexConfig? VectorIndexConfig { get; set; }
}
