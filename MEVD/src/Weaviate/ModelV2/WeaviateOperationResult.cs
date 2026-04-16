// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateOperationResult
{
    private const string Success = nameof(Success);

    [JsonPropertyName("errors")]
    public WeaviateOperationResultErrors? Errors { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonIgnore]
    public bool? IsSuccess => Status?.Equals(Success, StringComparison.OrdinalIgnoreCase);
}
