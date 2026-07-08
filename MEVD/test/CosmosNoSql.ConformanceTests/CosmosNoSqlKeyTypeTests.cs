// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlKeyTypeTests(CosmosNoSqlKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<CosmosNoSqlKeyTypeTests.Fixture>
{
    public new sealed class Fixture : KeyTypeTests.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlKeyTypeTests)));

        public override TestStore TestStore => _store.Value;
    }
}
