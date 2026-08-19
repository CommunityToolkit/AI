// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosDBMultiVectorModelTests(CosmosDBMultiVectorModelTests.Fixture fixture)
    : MultiVectorModelTests<string>(fixture), IClassFixture<CosmosDBMultiVectorModelTests.Fixture>
{
    public new sealed class Fixture : MultiVectorModelTests<string>.Fixture
    {
        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBMultiVectorModelTests)));
        
        public override TestStore TestStore => _store.Value;
    }
}
