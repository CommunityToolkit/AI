// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlBasicModelTests(CosmosNoSqlBasicModelTests.Fixture fixture)
    : BasicModelTests<string>(fixture), IClassFixture<CosmosNoSqlBasicModelTests.Fixture>
{
    public override Task SearchAsync_with_Skip()
    {
        Assert.SkipUnless(!((CosmosNoSqlTestStore)fixture.TestStore).UsesLocalEmulator, "The vNext emulator's DiskANN index does not support OFFSET in vector search.");

        return base.SearchAsync_with_Skip();
    }

    public new sealed class Fixture : BasicModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlBasicModelTests)));

        public override TestStore TestStore => _store.Value;
    }
}
