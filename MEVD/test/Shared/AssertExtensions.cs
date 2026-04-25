// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Assert = Xunit.Assert;

namespace CommunityToolkit.VectorData.UnitTests;

internal static class AssertExtensions
{
    /// <summary>Asserts that an exception is an <see cref="ArgumentOutOfRangeException"/> with the specified values.</summary>
    public static void AssertIsArgumentOutOfRange(Exception? e, string expectedParamName, string expectedActualValue)
    {
        ArgumentOutOfRangeException aoore = Assert.IsType<ArgumentOutOfRangeException>(e);
        Assert.Equal(expectedActualValue, aoore.ActualValue);
        Assert.Equal(expectedParamName, aoore.ParamName);
    }
}
