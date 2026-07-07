// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Cosmos.ConformanceTests;

public sealed class CosmosNoSqlNoVectorModelTests(CosmosNoSqlNoVectorModelTests.Fixture fixture)
    : NoVectorModelTests<string>(fixture), IClassFixture<CosmosNoSqlNoVectorModelTests.Fixture>
{
    public new sealed class Fixture : NoVectorModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlNoVectorModelTests)));
        
        public override TestStore TestStore => _store.Value;
    }
}
