// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosMongoDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosMongoDB.ConformanceTests.ModelTests;

public class CosmosMongoMultiVectorModelTests(CosmosMongoMultiVectorModelTests.Fixture fixture)
    : MultiVectorModelTests<string>(fixture), IClassFixture<CosmosMongoMultiVectorModelTests.Fixture>
{
    public new class Fixture : MultiVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosMongoTestStore.Instance;
    }
}
