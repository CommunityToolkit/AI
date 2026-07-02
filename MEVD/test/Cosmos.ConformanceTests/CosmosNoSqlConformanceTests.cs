// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Cosmos.ConformanceTests.Support;
using CommunityToolkit.VectorData.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace Cosmos.ConformanceTests;

public sealed class CosmosNoSqlDependencyInjectionTests
    : DependencyInjectionTests<CosmosNoSqlVectorStore, CosmosNoSqlCollection<string, DependencyInjectionTests<string>.Record>, string, DependencyInjectionTests<string>.Record>
{
    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates
    {
        get
        {
            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddCosmosNoSqlVectorStore(CosmosNoSqlTestStore.ConnectionString, CosmosNoSqlTestStore.DatabaseName, lifetime: lifetime)
                : services.AddKeyedCosmosNoSqlVectorStore(serviceKey, CosmosNoSqlTestStore.ConnectionString, CosmosNoSqlTestStore.DatabaseName, lifetime: lifetime);
        }
    }

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates
    {
        get
        {
            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddCosmosNoSqlCollection<string, Record>(name, CosmosNoSqlTestStore.ConnectionString, CosmosNoSqlTestStore.DatabaseName, lifetime: lifetime)
                : services.AddKeyedCosmosNoSqlCollection<string, Record>(serviceKey, name, CosmosNoSqlTestStore.ConnectionString, CosmosNoSqlTestStore.DatabaseName, lifetime: lifetime);
        }
    }

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey)
    {
    }
}

public sealed class CosmosNoSqlDistanceFunctionTests(CosmosNoSqlDistanceFunctionTests.Fixture fixture)
    : DistanceFunctionTests<string>(fixture), IClassFixture<CosmosNoSqlDistanceFunctionTests.Fixture>
{
    public override Task CosineDistance() => Assert.ThrowsAsync<NotSupportedException>(base.CosineDistance);
    public override Task EuclideanSquaredDistance() => Assert.ThrowsAsync<NotSupportedException>(base.EuclideanSquaredDistance);
    public override Task HammingDistance() => Assert.ThrowsAsync<NotSupportedException>(base.HammingDistance);
    public override Task ManhattanDistance() => Assert.ThrowsAsync<NotSupportedException>(base.ManhattanDistance);
    public override Task NegativeDotProductSimilarity() => Assert.ThrowsAsync<NotSupportedException>(base.NegativeDotProductSimilarity);

    public new sealed class Fixture : DistanceFunctionTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlEmbeddingGenerationTests(
    CosmosNoSqlEmbeddingGenerationTests.StringVectorFixture stringVectorFixture,
    CosmosNoSqlEmbeddingGenerationTests.RomOfFloatVectorFixture romOfFloatVectorFixture)
    : EmbeddingGenerationTests<string>(stringVectorFixture, romOfFloatVectorFixture),
        IClassFixture<CosmosNoSqlEmbeddingGenerationTests.StringVectorFixture>,
        IClassFixture<CosmosNoSqlEmbeddingGenerationTests.RomOfFloatVectorFixture>
{
    public new sealed class StringVectorFixture : EmbeddingGenerationTests<string>.StringVectorFixture
    {
        public override string DefaultIndexKind => "Flat";

        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosNoSqlConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosNoSqlVectorStore(
                CosmosNoSqlTestStore.Instance.Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services.AddCosmosNoSqlVectorStore(
                CosmosNoSqlTestStore.ConnectionString,
                CosmosNoSqlTestStore.DatabaseName,
                new() { JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions })
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services.AddCosmosNoSqlCollection<string, RecordWithAttributes>(
                CollectionName,
                CosmosNoSqlTestStore.ConnectionString,
                CosmosNoSqlTestStore.DatabaseName,
                new() { JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions })
        ];
    }

    public new sealed class RomOfFloatVectorFixture : EmbeddingGenerationTests<string>.RomOfFloatVectorFixture
    {
        public override string DefaultIndexKind => "Flat";

        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => CosmosNoSqlConformanceTestHelpers.UseLowerCaseVectorStorageName(base.CreateRecordDefinition(), "embedding");

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator = null)
            => new CosmosNoSqlVectorStore(
                CosmosNoSqlTestStore.Instance.Database,
                new() { EmbeddingGenerator = embeddingGenerator, JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions });

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services.AddCosmosNoSqlVectorStore(
                CosmosNoSqlTestStore.ConnectionString,
                CosmosNoSqlTestStore.DatabaseName,
                new() { JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions })
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services.AddCosmosNoSqlCollection<string, RecordWithAttributes>(
                CollectionName,
                CosmosNoSqlTestStore.ConnectionString,
                CosmosNoSqlTestStore.DatabaseName,
                new() { JsonSerializerOptions = CosmosNoSqlTestStore.SerializerOptions })
        ];
    }
}

public sealed class CosmosNoSqlFilterTests(CosmosNoSqlFilterTests.Fixture fixture)
    : FilterTests<string>(fixture), IClassFixture<CosmosNoSqlFilterTests.Fixture>
{
    public new sealed class Fixture : FilterTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlHybridSearchTests(
    CosmosNoSqlHybridSearchTests.VectorAndStringFixture vectorAndStringFixture,
    CosmosNoSqlHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<string>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<CosmosNoSqlHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<CosmosNoSqlHybridSearchTests.MultiTextFixture>
{
    public new sealed class VectorAndStringFixture : HybridSearchTests<string>.VectorAndStringFixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }

    public new sealed class MultiTextFixture : HybridSearchTests<string>.MultiTextFixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlIndexKindTests(CosmosNoSqlIndexKindTests.Fixture fixture)
    : IndexKindTests<string>(fixture), IClassFixture<CosmosNoSqlIndexKindTests.Fixture>
{
    public new sealed class Fixture : IndexKindTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlDataTypeTests(CosmosNoSqlDataTypeTests.Fixture fixture)
    : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>(fixture), IClassFixture<CosmosNoSqlDataTypeTests.Fixture>
{
    public override Task DateTimeOffset()
        => Task.CompletedTask;

    public new sealed class Fixture : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;

        public override Type[] UnsupportedDefaultTypes { get; } =
        [
            typeof(byte),
            typeof(short),
            typeof(decimal),
            typeof(Guid),
            typeof(TimeOnly)
        ];
    }
}

public sealed class CosmosNoSqlEmbeddingTypeTests(CosmosNoSqlEmbeddingTypeTests.Fixture fixture)
    : EmbeddingTypeTests<string>(fixture), IClassFixture<CosmosNoSqlEmbeddingTypeTests.Fixture>
{
    public new sealed class Fixture : EmbeddingTypeTests<string>.Fixture
    {
        public override string DefaultIndexKind => "Flat";

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

public sealed class CosmosNoSqlKeyTypeTests(CosmosNoSqlKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<CosmosNoSqlKeyTypeTests.Fixture>
{
    public new sealed class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

internal static class CosmosNoSqlConformanceTestHelpers
{
    public static VectorStoreCollectionDefinition UseLowerCaseVectorStorageName(
        VectorStoreCollectionDefinition definition,
        string storageName)
    {
        foreach (var vectorProperty in definition.Properties.OfType<VectorStoreVectorProperty>())
        {
            vectorProperty.StorageName = storageName;
        }

        return definition;
    }
}

public sealed class CosmosNoSqlBasicModelTests(CosmosNoSqlBasicModelTests.Fixture fixture)
    : BasicModelTests<string>(fixture), IClassFixture<CosmosNoSqlBasicModelTests.Fixture>
{
    public override async Task GetAsync_with_filter_and_multiple_OrderBys()
    {
        var exception = await Assert.ThrowsAsync<VectorStoreException>(base.GetAsync_with_filter_and_multiple_OrderBys);
        Assert.IsType<CosmosException>(exception.InnerException);
    }

    public new sealed class Fixture : BasicModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlDynamicModelTests(CosmosNoSqlDynamicModelTests.Fixture fixture)
    : DynamicModelTests<string>(fixture), IClassFixture<CosmosNoSqlDynamicModelTests.Fixture>
{
    public override async Task GetAsync_with_filter_and_multiple_OrderBys()
    {
        var exception = await Assert.ThrowsAsync<VectorStoreException>(base.GetAsync_with_filter_and_multiple_OrderBys);
        Assert.IsType<CosmosException>(exception.InnerException);
    }

    public new sealed class Fixture : DynamicModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlMultiVectorModelTests(CosmosNoSqlMultiVectorModelTests.Fixture fixture)
    : MultiVectorModelTests<string>(fixture), IClassFixture<CosmosNoSqlMultiVectorModelTests.Fixture>
{
    public new sealed class Fixture : MultiVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlNoDataModelTests(CosmosNoSqlNoDataModelTests.Fixture fixture)
    : NoDataModelTests<string>(fixture), IClassFixture<CosmosNoSqlNoDataModelTests.Fixture>
{
    public new sealed class Fixture : NoDataModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}

public sealed class CosmosNoSqlNoVectorModelTests(CosmosNoSqlNoVectorModelTests.Fixture fixture)
    : NoVectorModelTests<string>(fixture), IClassFixture<CosmosNoSqlNoVectorModelTests.Fixture>
{
    public new sealed class Fixture : NoVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}
