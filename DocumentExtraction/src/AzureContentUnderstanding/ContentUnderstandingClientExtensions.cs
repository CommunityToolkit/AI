// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.AI.ContentUnderstanding;
using Microsoft.Extensions.DocumentExtraction;

namespace CommunityToolkit.DocumentExtraction.AzureContentUnderstanding;

/// <summary>Converts a native Content Understanding client to a document extraction client.</summary>
public static class ContentUnderstandingClientExtensions
{
    /// <summary>Creates an extraction adapter that borrows <paramref name="client"/> and captures the supplied settings.</summary>
    public static DocumentExtractionClient AsDocumentExtractionClient(
        this ContentUnderstandingClient client, ContentUnderstandingExtractionOptions? options = null)
        => new ContentUnderstandingExtractionClient(client, options);
}
