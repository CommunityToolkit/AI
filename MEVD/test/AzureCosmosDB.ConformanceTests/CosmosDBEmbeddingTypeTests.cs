// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosDBEmbeddingTypeTests(CosmosDBEmbeddingTypeTests.Fixture fixture)
    : EmbeddingTypeTests<string>(fixture), IClassFixture<CosmosDBEmbeddingTypeTests.Fixture>
{
    public new sealed class Fixture : EmbeddingTypeTests<string>.Fixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBEmbeddingTypeTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition<TVectorProperty>(
            IEmbeddingGenerator? embeddingGenerator,
            string? distanceFunction,
            int dimensions)
            => CosmosDBConformanceTestHelpers.UseLowerCaseVectorStorageName(
                base.CreateRecordDefinition<TVectorProperty>(embeddingGenerator, distanceFunction, dimensions),
                "vector");
    }
}
