// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CommunityToolkit.VectorData.Cosmos;

internal static class Verify
{
#pragma warning disable CS8777 // Polyfilled nullable attributes are only used for older TFMs.
    [return: NotNull]
    public static T NotNull<T>([NotNull] T value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        return value!;
    }
#pragma warning restore CS8777

    public static string NotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        => value is null
            ? throw new ArgumentNullException(parameterName)
            : string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be empty, or consist only of white-space characters.", parameterName)
            : value;

    public static int NotLessThan(int value, int minimum, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        => value < minimum
            ? throw new ArgumentOutOfRangeException(parameterName, value, $"Value cannot be less than {minimum}.")
            : value;
}
