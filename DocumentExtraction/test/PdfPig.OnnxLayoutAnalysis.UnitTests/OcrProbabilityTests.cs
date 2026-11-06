// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime.Tensors;
using UglyToad.PdfPig.Content;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class OcrProbabilityTests
{
    [Theory]
    [InlineData(0x3F800002u)]
    [InlineData(0x3F800003u)]
    [InlineData(0x80000001u)]
    [InlineData(0xBF800000u)]
    [InlineData(0x7F800000u)]
    [InlineData(0xFF800000u)]
    [InlineData(0x7FC00000u)]
    [InlineData(0x7F7FFFFFu)]
    public void ProbabilityAuditRejectsEveryUnprovedBoundaryAndNonfiniteValue(uint bits)
    {
        var audit = new OcrProbabilityAudit();
        var error = Assert.Throws<InvalidDataException>(() => audit.Normalize(BitConverter.UInt32BitsToSingle(bits), 17));
        Assert.Contains($"0x{bits:X8}", error.Message);
        Assert.Equal(0, audit.AdjustmentCount);
        Assert.Null(audit.Observe(1, "detector", null, [1, 1, 1, 1]));
    }

    [Fact]
    public void ExactlyOneUpperUlpIsNormalizedWithDetachedOriginalEvidence()
    {
        float raw = BitConverter.UInt32BitsToSingle(0x3F800001);
        var audit = new OcrProbabilityAudit();
        Assert.Equal(.25f, audit.Normalize(.25f, 0));
        Assert.Equal(1, audit.Normalize(raw, 7));
        Assert.Equal(1, audit.Normalize(raw, 8));
        int[] dimensions = [1, 1, 3, 3];
        var observation = Assert.IsType<PdfPigOcrProbabilityAdjustment>(audit.Observe(4, "detector", null, dimensions));
        dimensions[2] = 999;
        audit.Normalize(raw, 9);
        Assert.Equal(4, observation.PageNumber);
        Assert.Equal("detector", observation.Stage);
        Assert.Null(observation.RegionIndex);
        Assert.Equal(new[] { 1, 1, 3, 3 }, observation.TensorDimensions);
        Assert.Equal(2, observation.AdjustmentCount);
        Assert.Equal(7, observation.FirstAdjustedIndex);
        Assert.Equal(0x3F800001u, BitConverter.SingleToUInt32Bits(observation.FirstRawValue));
        Assert.Equal(0x3F800001u, observation.FirstRawValueBits);
        Assert.Equal(.25f, observation.MinimumRawValue);
        Assert.Equal(raw, observation.MaximumRawValue);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)observation.TensorDimensions).Clear());
        var pipeline = new PdfPigPipelineObservation([], new Dictionary<int, IReadOnlyList<Word>>(), [], probabilityAdjustments: [observation]);
        Assert.Same(observation, Assert.Single(pipeline.OcrProbabilityAdjustments));
        Assert.Throws<NotSupportedException>(() => ((IList<PdfPigOcrProbabilityAdjustment>)pipeline.OcrProbabilityAdjustments).Clear());
    }

    [Fact]
    public void ValidEndpointsAndInteriorAreNotReportedAsNormalization()
    {
        var audit = new OcrProbabilityAudit();
        foreach (float raw in new[] { 0f, float.Epsilon, .3f, .5f, 1f })
        {
            Assert.Equal(raw, audit.Normalize(raw, 0));
        }

        Assert.Equal(0, audit.AdjustmentCount);
        Assert.Null(audit.Observe(1, "recognizer", 0, [1, 1, 5]));
    }

    [Fact]
    public void NormalizationDoesNotChangeDetectorOrScoreThresholdDecisions()
    {
        var audit = new OcrProbabilityAudit();
        foreach (float raw in new[] { float.BitDecrement(.3f), .3f, float.BitIncrement(.3f),
            float.BitDecrement(.5f), .5f, float.BitIncrement(.5f), 1f })
        {
            float normalized = audit.Normalize(raw, 0);
            Assert.Equal(raw, normalized);
            Assert.Equal(raw > .3f, normalized > .3f);
            Assert.Equal(raw >= .5f, normalized >= .5f);
        }

        Assert.Equal(0, audit.AdjustmentCount);
    }

    [Fact]
    public void CtcOneUlpNormalizationPreservesRawTensorSequenceAndReportedRegion()
    {
        float upper = BitConverter.UInt32BitsToSingle(0x3F800001);
        var probabilities = new DenseTensor<float>(new[] { .1f, upper, .2f, upper }, [1, 2, 2]);
        var original = probabilities.ToArray();
        var audit = new OcrProbabilityAudit();
        var actual = PaddleOcrModel.Decode(probabilities, ["", "A"], default, audit);
        Assert.Equal("A", actual.Text);
        Assert.Equal(1, actual.Confidence);
        Assert.Equal(original, probabilities.ToArray());
        var observation = Assert.IsType<PdfPigOcrProbabilityAdjustment>(audit.Observe(3, "recognizer", 2, [1, 2, 2]));
        Assert.Equal(2, observation.AdjustmentCount);
        Assert.Equal(1, observation.FirstAdjustedIndex);
        Assert.Equal("recognizer", observation.Stage);
        Assert.Equal(2, observation.RegionIndex);
        Assert.Equal(upper, observation.FirstRawValue);
        probabilities[0, 0, 1] = BitConverter.UInt32BitsToSingle(0x3F800002);
        Assert.Contains("step 0, class 1", Assert.Throws<InvalidDataException>(
            () => PaddleOcrModel.Decode(probabilities, ["", "A"], default)).Message);
    }
}
