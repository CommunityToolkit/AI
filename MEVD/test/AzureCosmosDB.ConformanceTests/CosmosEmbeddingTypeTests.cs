// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosEmbeddingTypeTests(CosmosEmbeddingTypeTests.Fixture fixture)
    : EmbeddingTypeTests<string>(fixture), IClassFixture<CosmosEmbeddingTypeTests.Fixture>
{
    public new sealed class Fixture : EmbeddingTypeTests<string>.Fixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosEmbeddingTypeTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition<TVectorProperty>(
            IEmbeddingGenerator? embeddingGenerator,
            string? distanceFunction,
            int dimensions)
            => CosmosConformanceTestHelpers.UseLowerCaseVectorStorageName(
                base.CreateRecordDefinition<TVectorProperty>(embeddingGenerator, distanceFunction, dimensions),
                "vector");
    }
}
