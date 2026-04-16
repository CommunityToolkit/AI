// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateDeleteObjectRequest(string collectionName, Guid id)
{
    private const string ApiRoute = "objects";

    [JsonIgnore]
    public string CollectionName { get; set; } = collectionName;

    [JsonIgnore]
    public Guid Id { get; set; } = id;

    public HttpRequestMessage Build()
    {
        return HttpRequest.CreateDeleteRequest($"{ApiRoute}/{CollectionName}/{Id}");
    }
}
