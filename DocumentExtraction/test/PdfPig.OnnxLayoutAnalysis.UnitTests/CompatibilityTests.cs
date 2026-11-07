// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class CompatibilityTests
{
    [Fact]
    public void NumericOperationsMatchModernBclIncludingExceptionalValues()
    {
        float[] singles = [float.NaN, float.NegativeInfinity, float.PositiveInfinity, float.MinValue, -10, -0f, 0, 10, float.MaxValue];
        foreach (float value in singles)
        {
            Assert.Equal(float.IsFinite(value), OnnxCompatibility.IsFinite(value));
            Assert.Equal(BitConverter.SingleToUInt32Bits(Math.Clamp(value, -10, 10)),
                OnnxCompatibility.SingleToUInt32Bits(OnnxCompatibility.Clamp(value, -10, 10)));
            Assert.Equal(BitConverter.SingleToUInt32Bits(value), OnnxCompatibility.SingleToUInt32Bits(value));
        }

        foreach (double value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, double.MinValue, -10, -0d, 0, 10, double.MaxValue })
        {
            Assert.Equal(double.IsFinite(value), OnnxCompatibility.IsFinite(value));
            Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Clamp(value, -10, 10)),
                BitConverter.DoubleToInt64Bits(OnnxCompatibility.Clamp(value, -10, 10)));
        }

        foreach (int value in new[] { int.MinValue, -10, 0, 10, int.MaxValue })
        {
            Assert.Equal(Math.Clamp(value, -10, 10), OnnxCompatibility.Clamp(value, -10, 10));
        }

        Assert.Throws<ArgumentException>(() => OnnxCompatibility.Clamp(0, 1, -1));
        Assert.Throws<ArgumentException>(() => OnnxCompatibility.Clamp(0f, 1f, -1f));
        Assert.Throws<ArgumentException>(() => OnnxCompatibility.Clamp(0d, 1d, -1d));
    }

    [Fact]
    public void ArtifactHashingPreservesExactUppercaseDigestAndStreamPosition()
    {
        byte[] bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), OnnxCompatibility.Sha256Hex(bytes));
        using var stream = new MemoryStream(bytes);
        stream.Position = 17;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(17))), OnnxCompatibility.Sha256Hex(stream));
        Assert.Equal(stream.Length, stream.Position);
    }
}
