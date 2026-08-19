// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosIndexKindTests(CosmosIndexKindTests.Fixture fixture)
    : IndexKindTests<string>(fixture), IClassFixture<CosmosIndexKindTests.Fixture>
{
    [Fact]
    public Task DiskAnn() => Test("DiskAnn");

    public override Task Flat()
    {
        Assert.SkipUnless(!((CosmosTestStore)fixture.TestStore).UsesLocalEmulator, "Not supported on emulator.");

        return base.Flat();
    }

    public new sealed class Fixture : IndexKindTests<string>.Fixture
    {
        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosIndexKindTests)));

        public override TestStore TestStore => _store.Value;
    }
}
