// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateGetCollectionsRequest
{
    private const string ApiRoute = "schema";

    public HttpRequestMessage Build()
    {
        return HttpRequest.CreateGetRequest(ApiRoute);
    }
}
