// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosDBBasicModelTests(CosmosDBBasicModelTests.Fixture fixture)
    : BasicModelTests<string>(fixture), IClassFixture<CosmosDBBasicModelTests.Fixture>
{
    public override Task SearchAsync_with_Skip()
    {
        Assert.SkipUnless(!((CosmosDBTestStore)fixture.TestStore).UsesLocalEmulator, "The vNext emulator's DiskANN index does not support OFFSET in vector search.");

        return base.SearchAsync_with_Skip();
    }

    public new sealed class Fixture : BasicModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBBasicModelTests)));

        public override TestStore TestStore => _store.Value;
    }
}
