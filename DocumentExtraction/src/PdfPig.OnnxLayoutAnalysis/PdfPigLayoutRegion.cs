// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>A detached model observation in the rotated, crop-relative PDF display frame.</summary>
public sealed class PdfPigLayoutRegion
{
    internal PdfPigLayoutRegion(int pageNumber, string label, float confidence, PdfRectangle bounds, IReadOnlyList<Word> words)
    {
        PageNumber = pageNumber;
        Label = label;
        Confidence = confidence;
        Bounds = bounds;
        Words = words;
    }

    /// <summary>Gets the one-based original PDF page number.</summary>
    public int PageNumber { get; }

    /// <summary>Gets the original Heron label.</summary>
    public string Label { get; }

    /// <summary>Gets the model confidence.</summary>
    public float Confidence { get; }

    /// <summary>Gets the bounds in points, with bottom-left origin, after page rotation and cropping.</summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Gets the native PdfPig words assigned to this region, in reading order.</summary>
    public IReadOnlyList<Word> Words { get; }
}
