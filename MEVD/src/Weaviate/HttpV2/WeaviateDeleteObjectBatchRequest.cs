// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateDeleteObjectBatchRequest
{
    private const string ApiRoute = "batch/objects";

    [JsonConstructor]
    public WeaviateDeleteObjectBatchRequest() { }

    public WeaviateDeleteObjectBatchRequest(WeaviateQueryMatch match)
    {
        Match = match;
    }

    [JsonPropertyName("match")]
    public WeaviateQueryMatch? Match { get; set; }

    public HttpRequestMessage Build()
    {
        return HttpRequest.CreateDeleteRequest(ApiRoute, this);
    }
}
