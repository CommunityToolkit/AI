// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Controls local page rendering and Heron layout detection.</summary>
public sealed class OnnxLayoutOptions
{
    /// <summary>Gets or sets the minimum retained detection confidence, between zero and one.</summary>
    public float ConfidenceThreshold { get; set; } = 0.5f;

    /// <summary>Gets or sets the render resolution in dots per inch, between 72 and 300.</summary>
    public int RenderDpi { get; set; } = 144;

    /// <summary>Gets or sets the maximum rendered pixels per page, before allocating a bitmap.</summary>
    public int MaximumPagePixels { get; set; } = 32_000_000;

    internal OnnxLayoutOptions Snapshot()
    {
        if (!float.IsFinite(ConfidenceThreshold) || ConfidenceThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ConfidenceThreshold));
        }

        if (RenderDpi is < 72 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(RenderDpi));
        }

        if (MaximumPagePixels <= 0 || MaximumPagePixels > int.MaxValue / 4)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPagePixels));
        }

        return new()
        {
            ConfidenceThreshold = ConfidenceThreshold,
            RenderDpi = RenderDpi,
            MaximumPagePixels = MaximumPagePixels,
        };
    }
}
