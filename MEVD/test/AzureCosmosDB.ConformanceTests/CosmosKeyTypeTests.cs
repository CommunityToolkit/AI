// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosKeyTypeTests(CosmosKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<CosmosKeyTypeTests.Fixture>
{
    public new sealed class Fixture : KeyTypeTests.Fixture
    {
        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosKeyTypeTests)));

        public override TestStore TestStore => _store.Value;
    }
}
