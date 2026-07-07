// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using CommunityToolkit.VectorData.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Cosmos.ConformanceTests;

public sealed class CosmosNoSqlEmbeddingGenerationTests(
    CosmosNoSqlEmbeddingGenerationTests.StringVectorFixture stringVectorFixture,
    CosmosNoSqlEmbeddingGenerationTests.RomOfFloatVectorFixture romOfFloatVectorFixture)
    : EmbeddingGenerationTests<string>(stringVectorFixture, romOfFloatVectorFixture),
        IClassFixture<CosmosNoSqlEmbeddingGenerationTests.StringVectorFixture>,
        IClassFixture<CosmosNoSqlEmbeddingGenerationTests.RomOfFloatVectorFixture>
{
    public new sealed class StringVectorFixture : EmbeddingGenerationTests<string>.StringVectorFixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlEmbeddingGenerationTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosNoSqlConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosNoSqlVectorStore(
                ((CosmosNoSqlTestStore)TestStore).Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services
                .AddSingleton(((CosmosNoSqlTestStore)TestStore).Database)
                .AddCosmosNoSqlVectorStore(new()
                {
                    JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions,
                }),
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddSingleton(((CosmosNoSqlTestStore)TestStore).Database)
                .AddCosmosNoSqlCollection<string, RecordWithAttributes>(CollectionName, new()
                {
                    JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions,
                }),
        ];
    }

    public new sealed class RomOfFloatVectorFixture : EmbeddingGenerationTests<string>.RomOfFloatVectorFixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlEmbeddingGenerationTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosNoSqlConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosNoSqlVectorStore(
                ((CosmosNoSqlTestStore)TestStore).Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services.AddCosmosNoSqlVectorStore(
                ((CosmosNoSqlTestStore)TestStore).ConnectionString,
                nameof(CosmosNoSqlEmbeddingGenerationTests),
                new() { JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions })
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services.AddCosmosNoSqlCollection<string, RecordWithAttributes>(
                CollectionName,
                ((CosmosNoSqlTestStore)TestStore).ConnectionString,
                nameof(CosmosNoSqlEmbeddingGenerationTests),
                new() { JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions })
        ];
    }
}
