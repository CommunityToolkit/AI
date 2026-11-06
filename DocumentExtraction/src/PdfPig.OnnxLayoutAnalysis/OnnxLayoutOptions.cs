// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Controls local page rendering and Heron layout detection.</summary>
public sealed class OnnxLayoutOptions
{
    /// <summary>Gets or sets the local Heron uint8 layout graph path. Required unless an explicit borrowed layout session is supplied.</summary>
    public string? LayoutModelPath { get; set; }

    /// <summary>Gets or sets the minimum retained detection confidence, between zero and one.</summary>
    public float ConfidenceThreshold { get; set; } = 0.5f;

    /// <summary>Gets or sets the render resolution in dots per inch, between 72 and 300.</summary>
    public int RenderDpi { get; set; } = 144;

    /// <summary>Gets or sets the maximum rendered pixels per page, before allocating a bitmap.</summary>
    public int MaximumPagePixels { get; set; } = 32_000_000;

    /// <summary>Gets or sets whether to order page elements with a left-to-right column-aware geometric heuristic. Defaults to false.</summary>
    public bool UseColumnReadingOrder { get; set; }

    /// <summary>Gets or sets whether to supply the pinned Heron graph's correct width-height target sizes. False preserves historical height-width geometry.</summary>
    public bool UseCorrectedLayoutCoordinates { get; set; }

    /// <summary>Gets or sets optional OCR configuration. Null disables OCR model loading and inference.</summary>
    public OnnxOcrOptions? Ocr { get; set; }

    /// <summary>Gets or sets borrowed ORT options used only during construction for sessions created by this client. Null selects the default CPU options. A supplied Heron session is unchanged.</summary>
    public SessionOptions? SessionOptions { get; set; }

    /// <summary>Gets or sets optional table structure configuration. Null disables table model loading and inference, independently of OCR.</summary>
    public OnnxTableStructureOptions? TableStructure { get; set; }

    internal OnnxLayoutOptions Snapshot(bool requireLayoutModel = false)
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

        if (requireLayoutModel)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(LayoutModelPath);
        }

        Ocr?.Validate();
        TableStructure?.Validate();
        return Copy();
    }

    internal OnnxLayoutOptions Copy() =>
        new()
        {
            LayoutModelPath = LayoutModelPath,
            ConfidenceThreshold = ConfidenceThreshold,
            RenderDpi = RenderDpi,
            MaximumPagePixels = MaximumPagePixels,
            UseColumnReadingOrder = UseColumnReadingOrder,
            UseCorrectedLayoutCoordinates = UseCorrectedLayoutCoordinates,
            Ocr = Ocr?.Copy(),
            SessionOptions = SessionOptions,
            TableStructure = TableStructure?.Copy(),
        };
}
