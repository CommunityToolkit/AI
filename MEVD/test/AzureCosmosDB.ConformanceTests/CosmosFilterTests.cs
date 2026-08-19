// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

// The type is internal to disable the tests due to emulator limitations
internal sealed class CosmosFilterTests(CosmosFilterTests.Fixture fixture)
    : FilterTests<string>(fixture), IClassFixture<CosmosFilterTests.Fixture>
{
    public new sealed class Fixture : FilterTests<string>.Fixture
    {
        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosFilterTests)));
        
        public override TestStore TestStore => _store.Value;
    }
}
