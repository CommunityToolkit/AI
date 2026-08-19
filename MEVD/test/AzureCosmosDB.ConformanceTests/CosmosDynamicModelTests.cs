// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosDynamicModelTests(CosmosDynamicModelTests.Fixture fixture)
    : DynamicModelTests<string>(fixture), IClassFixture<CosmosDynamicModelTests.Fixture>
{
    public override Task SearchAsync_with_Skip()
    {
        // The vNext emulator's DiskANN index does not support OFFSET in vector search.
        Assert.SkipUnless(!((CosmosTestStore)fixture.TestStore).UsesLocalEmulator, "The vNext emulator's DiskANN index does not support OFFSET in vector search.");

        return base.SearchAsync_with_Skip();
    }

    public new sealed class Fixture : DynamicModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosDynamicModelTests)));

        public override TestStore TestStore => _store.Value;
    }
}
