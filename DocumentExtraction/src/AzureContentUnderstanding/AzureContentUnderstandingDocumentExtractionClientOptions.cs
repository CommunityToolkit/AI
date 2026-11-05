// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.AzureContentUnderstanding;

/// <summary>Configures defaults for Azure Content Understanding extraction.</summary>
public sealed class AzureContentUnderstandingDocumentExtractionClientOptions
{
    /// <summary>Gets or sets the analyzer used when the extraction request does not select one.</summary>
    public string AnalyzerId { get; set; } = AzureContentUnderstandingDocumentExtractionClient.DefaultAnalyzerId;
}
