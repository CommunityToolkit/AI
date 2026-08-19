// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using CommunityToolkit.VectorData.AzureCosmosDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosEmbeddingGenerationTests(
    CosmosEmbeddingGenerationTests.StringVectorFixture stringVectorFixture,
    CosmosEmbeddingGenerationTests.RomOfFloatVectorFixture romOfFloatVectorFixture)
    : EmbeddingGenerationTests<string>(stringVectorFixture, romOfFloatVectorFixture),
        IClassFixture<CosmosEmbeddingGenerationTests.StringVectorFixture>,
        IClassFixture<CosmosEmbeddingGenerationTests.RomOfFloatVectorFixture>
{
    public new sealed class StringVectorFixture : EmbeddingGenerationTests<string>.StringVectorFixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosEmbeddingGenerationTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosVectorStore(
                ((CosmosTestStore)TestStore).Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services
                .AddSingleton(((CosmosTestStore)TestStore).Database)
                .AddCosmosVectorStore(new()
                {
                    JsonSerializerOptions = CosmosTestStore.SerializerOptions,
                }),
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddSingleton(((CosmosTestStore)TestStore).Database)
                .AddCosmosCollection<string, RecordWithAttributes>(CollectionName, new()
                {
                    JsonSerializerOptions = CosmosTestStore.SerializerOptions,
                }),
        ];
    }

    public new sealed class RomOfFloatVectorFixture : EmbeddingGenerationTests<string>.RomOfFloatVectorFixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosEmbeddingGenerationTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosVectorStore(
                ((CosmosTestStore)TestStore).Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services.AddCosmosVectorStore(
                ((CosmosTestStore)TestStore).ConnectionString,
                nameof(CosmosEmbeddingGenerationTests),
                new() { JsonSerializerOptions = CosmosTestStore.SerializerOptions })
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services.AddCosmosCollection<string, RecordWithAttributes>(
                CollectionName,
                ((CosmosTestStore)TestStore).ConnectionString,
                nameof(CosmosEmbeddingGenerationTests),
                new() { JsonSerializerOptions = CosmosTestStore.SerializerOptions })
        ];
    }
}
