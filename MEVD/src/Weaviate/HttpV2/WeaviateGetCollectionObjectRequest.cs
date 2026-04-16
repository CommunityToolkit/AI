// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateGetCollectionObjectRequest(string collectionName, Guid id, bool includeVectors)
{
    private const string ApiRoute = "objects";
    private const string IncludeQueryParameterName = "include";
    private const string IncludeVectorQueryParameterValue = "vector";

    [JsonIgnore]
    public string CollectionName { get; set; } = collectionName;

    [JsonIgnore]
    public Guid Id { get; set; } = id;

    [JsonIgnore]
    public bool IncludeVectors { get; set; } = includeVectors;

    public HttpRequestMessage Build()
    {
        var uri = $"{ApiRoute}/{CollectionName}/{Id}";

        if (IncludeVectors)
        {
            uri += $"?{IncludeQueryParameterName}={IncludeVectorQueryParameterValue}";
        }

        return HttpRequest.CreateGetRequest(uri);
    }
}
