// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosDocumentDB.ConformanceTests.Support;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosDocumentDB.ConformanceTests;

public class CosmosDocumentDBIndexKindTests(CosmosDocumentDBIndexKindTests.Fixture fixture)
    : IndexKindTests<int>(fixture), IClassFixture<CosmosDocumentDBIndexKindTests.Fixture>
{
    // Note: Cosmos DocumentDB supports HNSW, but only in a specific tier.
    // [Fact]
    // public virtual Task Hnsw()
    //     => this.Test(IndexKind.Hnsw);

    [Fact]
    public virtual Task IvfFlat()
        => this.Test(IndexKind.IvfFlat);

    // Cosmos DocumentDB does not support index-less searching
    public override Task Flat() => Assert.ThrowsAsync<NotSupportedException>(base.Flat);

    public new class Fixture() : IndexKindTests<int>.Fixture
    {
        public override TestStore TestStore => CosmosDocumentDBTestStore.Instance;
    }
}
