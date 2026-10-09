// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Chroma.ConformanceTests;

public class ChromaDistanceFunctionTests(ChromaDistanceFunctionTests.Fixture fixture)
    : DistanceFunctionTests<string>(fixture), IClassFixture<ChromaDistanceFunctionTests.Fixture>
{
    // Chroma supports cosine, inner product (ip) and squared Euclidean (l2) only
    public override Task ManhattanDistance() => Assert.ThrowsAsync<NotSupportedException>(base.ManhattanDistance);
    public override Task HammingDistance() => Assert.ThrowsAsync<NotSupportedException>(base.HammingDistance);

    public new class Fixture() : DistanceFunctionTests<string>.Fixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }
}
