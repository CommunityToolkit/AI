// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlIndexKindTests(CosmosNoSqlIndexKindTests.Fixture fixture)
    : IndexKindTests<string>(fixture), IClassFixture<CosmosNoSqlIndexKindTests.Fixture>
{
    [Fact]
    public Task DiskAnn() => Test("DiskAnn");

    public override Task Flat()
    {
        Assert.SkipUnless(!((CosmosNoSqlTestStore)fixture.TestStore).UsesLocalEmulator, "Not supported on emulator.");

        return base.Flat();
    }

    public new sealed class Fixture : IndexKindTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlIndexKindTests)));

        public override TestStore TestStore => _store.Value;
    }
}
