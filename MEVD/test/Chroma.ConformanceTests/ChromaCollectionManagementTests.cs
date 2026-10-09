// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using CommunityToolkit.VectorData.Chroma;
using VectorData.ConformanceTests;
using Xunit;

namespace Chroma.ConformanceTests;

public class ChromaCollectionManagementTests(ChromaFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<ChromaFixture>
{
}
