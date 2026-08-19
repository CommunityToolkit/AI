// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosDBKeyTypeTests(CosmosDBKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<CosmosDBKeyTypeTests.Fixture>
{
    public new sealed class Fixture : KeyTypeTests.Fixture
    {
        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBKeyTypeTests)));

        public override TestStore TestStore => _store.Value;
    }
}
