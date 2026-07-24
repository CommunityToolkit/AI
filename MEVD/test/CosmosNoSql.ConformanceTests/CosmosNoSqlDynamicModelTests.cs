// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlDynamicModelTests(CosmosNoSqlDynamicModelTests.Fixture fixture)
    : DynamicModelTests<string>(fixture), IClassFixture<CosmosNoSqlDynamicModelTests.Fixture>
{
    public override Task SearchAsync_with_Skip()
    {
        // The vNext emulator's DiskANN index does not support OFFSET in vector search.
        Assert.SkipUnless(!((CosmosNoSqlTestStore)fixture.TestStore).UsesLocalEmulator, "The vNext emulator's DiskANN index does not support OFFSET in vector search.");

        return base.SearchAsync_with_Skip();
    }

    public new sealed class Fixture : DynamicModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlDynamicModelTests)));

        public override TestStore TestStore => _store.Value;
    }
}
