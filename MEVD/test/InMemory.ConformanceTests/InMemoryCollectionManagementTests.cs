// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using InMemory.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace InMemory.ConformanceTests;

public class InMemoryCollectionManagementTests(InMemoryFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<InMemoryFixture>
{
}
