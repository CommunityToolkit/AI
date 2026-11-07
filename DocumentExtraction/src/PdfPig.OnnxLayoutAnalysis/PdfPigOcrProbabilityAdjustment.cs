// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Detached evidence of a one-ULP-above-one float32 OCR probability normalized to one.</summary>
public sealed class PdfPigOcrProbabilityAdjustment
{
    internal PdfPigOcrProbabilityAdjustment(
        int pageNumber, string stage, int? regionIndex, int[] dimensions, OcrProbabilityAudit audit)
    {
        PageNumber = pageNumber;
        Stage = stage;
        RegionIndex = regionIndex;
        TensorDimensions = Array.AsReadOnly(dimensions.ToArray());
        AdjustmentCount = audit.AdjustmentCount;
        FirstAdjustedIndex = audit.FirstAdjustedIndex;
        FirstRawValue = audit.FirstRawValue;
        MinimumRawValue = audit.MinimumRawValue;
        MaximumRawValue = audit.MaximumRawValue;
    }

    /// <summary>Gets the actual one-based PDF page number.</summary>
    public int PageNumber { get; }
    /// <summary>Gets the affected pinned model stage: detector or recognizer.</summary>
    public string Stage { get; }
    /// <summary>Gets the zero-based page OCR region index for recognition, or null for detection.</summary>
    public int? RegionIndex { get; }
    /// <summary>Gets the original tensor dimensions used to interpret the flattened index.</summary>
    public IReadOnlyList<int> TensorDimensions { get; }
    /// <summary>Gets the number of adjusted tensor values, counted once each.</summary>
    public int AdjustmentCount { get; }
    /// <summary>Gets the row-major flattened index of the first adjusted original value.</summary>
    public int FirstAdjustedIndex { get; }
    /// <summary>Gets the first original float32 value without normalization.</summary>
    public float FirstRawValue { get; }
    /// <summary>Gets the exact original float32 bit pattern of the first adjusted value.</summary>
    public uint FirstRawValueBits => OnnxCompatibility.SingleToUInt32Bits(FirstRawValue);
    /// <summary>Gets the minimum original tensor value without normalization.</summary>
    public float MinimumRawValue { get; }
    /// <summary>Gets the maximum original tensor value without normalization.</summary>
    public float MaximumRawValue { get; }
}
