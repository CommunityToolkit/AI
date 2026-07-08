// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlMultiVectorModelTests(CosmosNoSqlMultiVectorModelTests.Fixture fixture)
    : MultiVectorModelTests<string>(fixture), IClassFixture<CosmosNoSqlMultiVectorModelTests.Fixture>
{
    public new sealed class Fixture : MultiVectorModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlMultiVectorModelTests)));
        
        public override TestStore TestStore => _store.Value;
    }
}
