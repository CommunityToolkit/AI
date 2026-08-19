// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

// The type is internal to disable the tests due to emulator limitations
internal sealed class CosmosDBFilterTests(CosmosDBFilterTests.Fixture fixture)
    : FilterTests<string>(fixture), IClassFixture<CosmosDBFilterTests.Fixture>
{
    public new sealed class Fixture : FilterTests<string>.Fixture
    {
        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBFilterTests)));
        
        public override TestStore TestStore => _store.Value;
    }
}
