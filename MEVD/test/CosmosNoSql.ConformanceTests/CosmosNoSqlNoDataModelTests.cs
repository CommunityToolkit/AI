// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlNoDataModelTests(CosmosNoSqlNoDataModelTests.Fixture fixture)
    : NoDataModelTests<string>(fixture), IClassFixture<CosmosNoSqlNoDataModelTests.Fixture>
{
    public new sealed class Fixture : NoDataModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlNoDataModelTests)));
        
        public override TestStore TestStore => _store.Value;
    }
}
