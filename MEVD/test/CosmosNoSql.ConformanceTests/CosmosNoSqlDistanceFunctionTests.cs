// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlDistanceFunctionTests(CosmosNoSqlDistanceFunctionTests.Fixture fixture)
    : DistanceFunctionTests<string>(fixture), IClassFixture<CosmosNoSqlDistanceFunctionTests.Fixture>
{
    public override Task CosineDistance() => Assert.ThrowsAsync<NotSupportedException>(base.CosineDistance);
    public override Task EuclideanSquaredDistance() => Assert.ThrowsAsync<NotSupportedException>(base.EuclideanSquaredDistance);
    public override Task HammingDistance() => Assert.ThrowsAsync<NotSupportedException>(base.HammingDistance);
    public override Task ManhattanDistance() => Assert.ThrowsAsync<NotSupportedException>(base.ManhattanDistance);
    public override Task NegativeDotProductSimilarity() => Assert.ThrowsAsync<NotSupportedException>(base.NegativeDotProductSimilarity);

    public override Task EuclideanDistance()
    {
        Assert.SkipUnless(!((CosmosNoSqlTestStore)fixture.TestStore).UsesLocalEmulator, "Fails on emulator, most likely due to emulator bug");

        return base.EuclideanDistance();
    }

    public new sealed class Fixture : DistanceFunctionTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlDistanceFunctionTests)));

        public override TestStore TestStore => _store.Value;
    }
}
