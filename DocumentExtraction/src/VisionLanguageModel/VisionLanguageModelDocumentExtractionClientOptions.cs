// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.VisionLanguageModel;

/// <summary>Configures vision language model transcription behavior.</summary>
public sealed class VisionLanguageModelDocumentExtractionClientOptions
{
    /// <summary>Gets or sets the transcription instruction. Null selects the built-in prompt for the chosen output mode.</summary>
    public string? Prompt { get; set; }

    /// <summary>Gets or sets whether to request and validate structured output instead of free-form transcription.</summary>
    public bool UseStructuredOutput { get; set; }
}
