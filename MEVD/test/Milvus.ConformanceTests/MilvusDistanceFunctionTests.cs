// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;
namespace Milvus.ConformanceTests;
public class MilvusDistanceFunctionTests(MilvusDistanceFunctionTests.Fixture fixture) : DistanceFunctionTests<string>(fixture), IClassFixture<MilvusDistanceFunctionTests.Fixture>
{
    public override Task NegativeDotProductSimilarity() => Assert.ThrowsAsync<NotSupportedException>(base.NegativeDotProductSimilarity);
    public override Task ManhattanDistance() => Assert.ThrowsAsync<NotSupportedException>(base.ManhattanDistance);
    public override Task HammingDistance() => Assert.ThrowsAsync<NotSupportedException>(base.HammingDistance);
    public override Task CosineDistance() => Assert.ThrowsAsync<NotSupportedException>(base.CosineDistance);
    public new class Fixture : DistanceFunctionTests<string>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}
