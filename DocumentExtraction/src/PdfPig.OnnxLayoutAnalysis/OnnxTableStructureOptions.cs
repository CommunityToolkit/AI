// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Enables the pinned Table Transformer inside actual Heron table regions.</summary>
public sealed class OnnxTableStructureOptions
{
    /// <summary>Gets or sets the required local pinned table structure model path.</summary>
    public string? ModelPath { get; set; }

    internal void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(ModelPath);

    internal OnnxTableStructureOptions Copy() => new() { ModelPath = ModelPath };
}
