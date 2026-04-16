// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateGetCollectionSchemaRequest(string collectionName)
{
    private const string ApiRoute = "schema";

    [JsonIgnore]
    public string CollectionName { get; set; } = collectionName;

    public HttpRequestMessage Build()
    {
        return HttpRequest.CreateGetRequest($"{ApiRoute}/{CollectionName}");
    }
}
