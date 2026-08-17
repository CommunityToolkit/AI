// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosDocumentDB.ConformanceTests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosDocumentDB.ConformanceTests;

public class CosmosDocumentDBEmbeddingGenerationTests(CosmosDocumentDBEmbeddingGenerationTests.StringVectorFixture stringVectorFixture, CosmosDocumentDBEmbeddingGenerationTests.RomOfFloatVectorFixture romOfFloatVectorFixture)
    : EmbeddingGenerationTests<string>(stringVectorFixture, romOfFloatVectorFixture), IClassFixture<CosmosDocumentDBEmbeddingGenerationTests.StringVectorFixture>, IClassFixture<CosmosDocumentDBEmbeddingGenerationTests.RomOfFloatVectorFixture>
{
    public new class StringVectorFixture : EmbeddingGenerationTests<string>.StringVectorFixture
    {
        public override TestStore TestStore => CosmosDocumentDBTestStore.Instance;

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator)
            => CosmosDocumentDBTestStore.Instance.GetVectorStore(new() { EmbeddingGenerator = embeddingGenerator });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services
                .AddSingleton(CosmosDocumentDBTestStore.Instance.Database)
                .AddCosmosDocumentDBVectorStore()
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddSingleton(CosmosDocumentDBTestStore.Instance.Database)
                .AddCosmosDocumentDBCollection<RecordWithAttributes>(this.CollectionName)
        ];
    }

    public new class RomOfFloatVectorFixture : EmbeddingGenerationTests<string>.RomOfFloatVectorFixture
    {
        public override TestStore TestStore => CosmosDocumentDBTestStore.Instance;

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator)
            => CosmosDocumentDBTestStore.Instance.GetVectorStore(new() { EmbeddingGenerator = embeddingGenerator });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services
                .AddSingleton(CosmosDocumentDBTestStore.Instance.Database)
                .AddCosmosDocumentDBVectorStore()
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddSingleton(CosmosDocumentDBTestStore.Instance.Database)
                .AddCosmosDocumentDBCollection<RecordWithAttributes>(this.CollectionName)
        ];
    }
}
