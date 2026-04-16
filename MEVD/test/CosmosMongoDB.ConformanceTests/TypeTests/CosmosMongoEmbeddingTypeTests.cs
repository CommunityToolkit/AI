// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosMongoDB.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

#pragma warning disable CA2000 // Dispose objects before losing scope

namespace CosmosMongoDB.ConformanceTests.TypeTests;

public class CosmosMongoEmbeddingTypeTests(CosmosMongoEmbeddingTypeTests.Fixture fixture)
    : EmbeddingTypeTests<string>(fixture), IClassFixture<CosmosMongoEmbeddingTypeTests.Fixture>
{
    public new class Fixture : EmbeddingTypeTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosMongoTestStore.Instance;
    }
}
