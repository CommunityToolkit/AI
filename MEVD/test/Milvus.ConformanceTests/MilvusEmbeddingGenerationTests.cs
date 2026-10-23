// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using Milvus.Client;
using CommunityToolkit.VectorData.Milvus;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests;

public class MilvusEmbeddingGenerationTests(MilvusEmbeddingGenerationTests.StringVectorFixture stringVectorFixture, MilvusEmbeddingGenerationTests.RomOfFloatVectorFixture romOfFloatVectorFixture)
    : EmbeddingGenerationTests<Guid>(stringVectorFixture, romOfFloatVectorFixture), IClassFixture<MilvusEmbeddingGenerationTests.StringVectorFixture>, IClassFixture<MilvusEmbeddingGenerationTests.RomOfFloatVectorFixture>
{
    public new class StringVectorFixture : EmbeddingGenerationTests<Guid>.StringVectorFixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator)
            => MilvusTestStore.Instance.GetVectorStore(new() { EmbeddingGenerator = embeddingGenerator });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services
                .AddSingleton(MilvusTestStore.Instance.Client)
                .AddMilvusVectorStore()
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddSingleton(MilvusTestStore.Instance.Client)
                .AddMilvusCollection<Guid, RecordWithAttributes>(this.CollectionName)
        ];
    }

    public new class RomOfFloatVectorFixture : EmbeddingGenerationTests<Guid>.RomOfFloatVectorFixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator)
            => MilvusTestStore.Instance.GetVectorStore(new() { EmbeddingGenerator = embeddingGenerator });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services
                .AddSingleton(MilvusTestStore.Instance.Client)
                .AddMilvusVectorStore()
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddSingleton(MilvusTestStore.Instance.Client)
                .AddMilvusCollection<Guid, RecordWithAttributes>(this.CollectionName)
        ];
    }
}
