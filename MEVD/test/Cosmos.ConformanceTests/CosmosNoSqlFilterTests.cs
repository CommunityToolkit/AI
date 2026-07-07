// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Cosmos.ConformanceTests;

// The type is internal to disable the tests due to emulator limitations
internal sealed class CosmosNoSqlFilterTests(CosmosNoSqlFilterTests.Fixture fixture)
    : FilterTests<string>(fixture), IClassFixture<CosmosNoSqlFilterTests.Fixture>
{
    public new sealed class Fixture : FilterTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlFilterTests)));
        
        public override TestStore TestStore => _store.Value   ;
    }
}
