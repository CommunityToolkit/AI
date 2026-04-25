// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

/// <summary>
/// Vector search request.
/// More information here: <see href="https://weaviate.io/developers/weaviate/api/graphql"/>.
/// </summary>
internal sealed class WeaviateVectorSearchRequest(string query)
{
    private const string ApiRoute = "graphql";

    [JsonPropertyName("query")]
    public string Query { get; set; } = query;

    public HttpRequestMessage Build()
    {
        return HttpRequest.CreatePostRequest(ApiRoute, this);
    }
}
