// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;

namespace CommunityToolkit.DocumentExtraction;

internal static class OnnxCompatibility
{
    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    internal static int Clamp(int value, int minimum, int maximum)
    {
#if NETSTANDARD2_0
        if (minimum > maximum)
        {
            throw new ArgumentException("Minimum cannot exceed maximum.");
        }

        return value < minimum ? minimum : value > maximum ? maximum : value;
#else
        return Math.Clamp(value, minimum, maximum);
#endif
    }

    internal static float Clamp(float value, float minimum, float maximum)
    {
#if NETSTANDARD2_0
        if (minimum > maximum)
        {
            throw new ArgumentException("Minimum cannot exceed maximum.");
        }

        return value < minimum ? minimum : value > maximum ? maximum : value;
#else
        return Math.Clamp(value, minimum, maximum);
#endif
    }

    internal static double Clamp(double value, double minimum, double maximum)
    {
#if NETSTANDARD2_0
        if (minimum > maximum)
        {
            throw new ArgumentException("Minimum cannot exceed maximum.");
        }

        return value < minimum ? minimum : value > maximum ? maximum : value;
#else
        return Math.Clamp(value, minimum, maximum);
#endif
    }

    internal static uint SingleToUInt32Bits(float value)
    {
#if NETSTANDARD2_0
        return BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
#else
        return BitConverter.SingleToUInt32Bits(value);
#endif
    }

    internal static string Sha256Hex(Stream stream)
    {
#if NETSTANDARD2_0
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
#else
        return Convert.ToHexString(SHA256.HashData(stream));
#endif
    }

    internal static string Sha256Hex(byte[] bytes)
    {
#if NETSTANDARD2_0
        using var stream = new MemoryStream(bytes, writable: false);
        return Sha256Hex(stream);
#else
        return Convert.ToHexString(SHA256.HashData(bytes));
#endif
    }
}
