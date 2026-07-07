// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace Cosmos.ConformanceTests;

public sealed class CosmosNoSqlEmbeddingTypeTests(CosmosNoSqlEmbeddingTypeTests.Fixture fixture)
    : EmbeddingTypeTests<string>(fixture), IClassFixture<CosmosNoSqlEmbeddingTypeTests.Fixture>
{
    public new sealed class Fixture : EmbeddingTypeTests<string>.Fixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;

        public override VectorStoreCollectionDefinition CreateRecordDefinition<TVectorProperty>(
            IEmbeddingGenerator? embeddingGenerator,
            string? distanceFunction,
            int dimensions)
            => CosmosNoSqlConformanceTestHelpers.UseLowerCaseVectorStorageName(
                base.CreateRecordDefinition<TVectorProperty>(embeddingGenerator, distanceFunction, dimensions),
                "vector");
    }
}
