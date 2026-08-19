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

public sealed class CosmosDBEmbeddingGenerationTests(
    CosmosDBEmbeddingGenerationTests.StringVectorFixture stringVectorFixture,
    CosmosDBEmbeddingGenerationTests.RomOfFloatVectorFixture romOfFloatVectorFixture)
    : EmbeddingGenerationTests<string>(stringVectorFixture, romOfFloatVectorFixture),
        IClassFixture<CosmosDBEmbeddingGenerationTests.StringVectorFixture>,
        IClassFixture<CosmosDBEmbeddingGenerationTests.RomOfFloatVectorFixture>
{
    public new sealed class StringVectorFixture : EmbeddingGenerationTests<string>.StringVectorFixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBEmbeddingGenerationTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosDBConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosDBVectorStore(
                ((CosmosDBTestStore)TestStore).Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosDBTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services
                .AddSingleton(((CosmosDBTestStore)TestStore).Database)
                .AddCosmosDBVectorStore(new()
                {
                    JsonSerializerOptions = CosmosDBTestStore.SerializerOptions,
                }),
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddSingleton(((CosmosDBTestStore)TestStore).Database)
                .AddCosmosDBCollection<string, RecordWithAttributes>(CollectionName, new()
                {
                    JsonSerializerOptions = CosmosDBTestStore.SerializerOptions,
                }),
        ];
    }

    public new sealed class RomOfFloatVectorFixture : EmbeddingGenerationTests<string>.RomOfFloatVectorFixture
    {
        public override string DefaultIndexKind => "DiskAnn";

        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBEmbeddingGenerationTests)));

        public override TestStore TestStore => _store.Value;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosDBConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosDBVectorStore(
                ((CosmosDBTestStore)TestStore).Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosDBTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services.AddCosmosDBVectorStore(
                ((CosmosDBTestStore)TestStore).ConnectionString,
                nameof(CosmosDBEmbeddingGenerationTests),
                new() { JsonSerializerOptions = CosmosDBTestStore.SerializerOptions })
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services.AddCosmosDBCollection<string, RecordWithAttributes>(
                CollectionName,
                ((CosmosDBTestStore)TestStore).ConnectionString,
                nameof(CosmosDBEmbeddingGenerationTests),
                new() { JsonSerializerOptions = CosmosDBTestStore.SerializerOptions })
        ];
    }
}
