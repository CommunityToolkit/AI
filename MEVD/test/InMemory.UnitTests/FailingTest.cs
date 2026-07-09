// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace InMemory.UnitTests;

/// <summary>
/// Temporary test used to verify that CI correctly reports unit test failures.
/// This file should be removed once the CI failure reporting has been confirmed.
/// </summary>
public class FailingTest
{
    [Fact]
    public void ThisTestIntentionallyFails()
    {
        Assert.Fail("This test is intentionally failing to verify CI failure reporting.");
    }
}
