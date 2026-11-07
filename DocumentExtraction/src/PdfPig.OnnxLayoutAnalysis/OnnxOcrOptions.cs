// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Enables the pinned PP-OCRv5 mobile detector and general recognizer with one text authority per page.</summary>
public sealed class OnnxOcrOptions
{
    /// <summary>Gets or sets the required local pinned detector path.</summary>
    public string? DetectorModelPath { get; set; }

    /// <summary>Gets or sets the required local pinned general recognizer path, including its embedded dictionary.</summary>
    public string? RecognizerModelPath { get; set; }

    /// <summary>Gets or sets when OCR owns page text. Defaults to pages without any visible native words.</summary>
    public OnnxOcrMode Mode { get; set; } = OnnxOcrMode.PagesWithoutNativeText;

    internal void Validate()
    {
        if (!Enum.IsDefined(typeof(OnnxOcrMode), Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode));
        }

        Throw.IfNullOrWhitespace(DetectorModelPath);
        Throw.IfNullOrWhitespace(RecognizerModelPath);
    }

    internal OnnxOcrOptions Copy() => new()
    {
        DetectorModelPath = DetectorModelPath,
        RecognizerModelPath = RecognizerModelPath,
        Mode = Mode,
    };
}
