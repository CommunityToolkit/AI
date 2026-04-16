// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateCreateCollectionSchemaRequest
{
    private const string ApiRoute = "schema";

    [JsonConstructor]
    public WeaviateCreateCollectionSchemaRequest() { }

    public WeaviateCreateCollectionSchemaRequest(WeaviateCollectionSchema collectionSchema)
    {
        CollectionName = collectionSchema.CollectionName;
        VectorConfigurations = collectionSchema.VectorConfigurations;
        Properties = collectionSchema.Properties;
    }

    [JsonPropertyName("class")]
    public string? CollectionName { get; set; }

    [JsonPropertyName("vectorConfig")]
    public Dictionary<string, WeaviateCollectionSchemaVectorConfig>? VectorConfigurations { get; set; }

    [JsonPropertyName("properties")]
    public List<WeaviateCollectionSchemaProperty>? Properties { get; set; }

    public HttpRequestMessage Build()
    {
        return HttpRequest.CreatePostRequest(ApiRoute, this);
    }
}
