// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosNoVectorModelTests(CosmosNoVectorModelTests.Fixture fixture)
    : NoVectorModelTests<string>(fixture), IClassFixture<CosmosNoVectorModelTests.Fixture>
{
    public new sealed class Fixture : NoVectorModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosNoVectorModelTests)));
        
        public override TestStore TestStore => _store.Value;
    }
}
