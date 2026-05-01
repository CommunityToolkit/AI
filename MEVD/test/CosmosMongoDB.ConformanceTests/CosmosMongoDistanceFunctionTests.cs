// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosMongoDB.ConformanceTests.Support;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosMongoDB.ConformanceTests;

public class CosmosMongoDistanceFunctionTests(CosmosMongoDistanceFunctionTests.Fixture fixture)
    : DistanceFunctionTests<int>(fixture), IClassFixture<CosmosMongoDistanceFunctionTests.Fixture>
{
    public override Task CosineSimilarity() => Assert.ThrowsAsync<NotSupportedException>(base.CosineSimilarity);
    public override Task EuclideanSquaredDistance() => Assert.ThrowsAsync<NotSupportedException>(base.EuclideanSquaredDistance);
    public override Task NegativeDotProductSimilarity() => Assert.ThrowsAsync<NotSupportedException>(base.NegativeDotProductSimilarity);
    public override Task HammingDistance() => Assert.ThrowsAsync<NotSupportedException>(base.HammingDistance);
    public override Task ManhattanDistance() => Assert.ThrowsAsync<NotSupportedException>(base.ManhattanDistance);

    // CosmosMongo EuclideanDistance doesn't correctly filter by score threshold (returns all results).
    // See https://github.com/CommunityToolkit/AI/issues/6.
    protected override Task TestScoreThreshold(VectorStoreCollection<int, DistanceFunctionTests<int>.SearchRecord> collection)
        => Task.CompletedTask;

    public new class Fixture() : DistanceFunctionTests<int>.Fixture
    {
        public override TestStore TestStore => CosmosMongoTestStore.Instance;

        public override bool AssertScores { get; } = false;
    }
}
