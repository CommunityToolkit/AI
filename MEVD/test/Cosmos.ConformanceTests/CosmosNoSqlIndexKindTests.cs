// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Cosmos.ConformanceTests;

public sealed class CosmosNoSqlIndexKindTests(CosmosNoSqlIndexKindTests.Fixture fixture)
    : IndexKindTests<string>(fixture), IClassFixture<CosmosNoSqlIndexKindTests.Fixture>
{
    [Fact]
    public Task DiskAnn() => Test("DiskAnn");

    public override Task Flat()
    {
        if (!CosmosNoSqlTestStore.Instance.UsesLocalEmulator)
        {
            return base.Flat();
        }

        return Task.CompletedTask;
    }

    public new sealed class Fixture : IndexKindTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}
