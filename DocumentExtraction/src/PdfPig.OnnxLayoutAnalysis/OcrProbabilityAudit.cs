// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal sealed class OcrProbabilityAudit
{
    private static readonly float s_maximum = float.BitIncrement(1f);
    internal int AdjustmentCount { get; private set; }
    internal int FirstAdjustedIndex { get; private set; } = -1;
    internal float MinimumRawValue { get; private set; } = float.PositiveInfinity;
    internal float MaximumRawValue { get; private set; } = float.NegativeInfinity;
    internal float FirstRawValue { get; private set; }

    internal float Normalize(float value, int flattenedIndex)
    {
        if (!float.IsFinite(value) || value < 0 || value > s_maximum)
        {
            throw new InvalidDataException(
                $"Invalid OCR probability {value:R} (float32 bits 0x{BitConverter.SingleToUInt32Bits(value):X8}).");
        }

        MinimumRawValue = Math.Min(MinimumRawValue, value);
        MaximumRawValue = Math.Max(MaximumRawValue, value);
        if (value > 1)
        {
            if (AdjustmentCount == 0)
            {
                FirstAdjustedIndex = flattenedIndex;
                FirstRawValue = value;
            }

            AdjustmentCount++;
            return 1;
        }

        return value;
    }

    internal PdfPigOcrProbabilityAdjustment? Observe(int pageNumber, string stage, int? regionIndex, int[] dimensions) =>
        AdjustmentCount == 0 ? null : new(pageNumber, stage, regionIndex, dimensions, this);
}
