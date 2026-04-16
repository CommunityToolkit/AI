// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using Pinecone.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace Pinecone.ConformanceTests.TypeTests;

public class PineconeKeyTypeTests(PineconeKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<PineconeKeyTypeTests.Fixture>
{
    [Fact]
    public virtual Task String() => this.Test<string>("foo", "bar");

    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => PineconeTestStore.Instance;

        // The Pinecone local emulator has eventual consistency, so deleting and recreating
        // a collection with the same name but different key types can cause stale data from
        // the previous test to bleed through. Use unique collection names per key type.
        public override VectorStoreCollection<TKey, Record<TKey>> CreateCollection<TKey>(bool? withAutoGeneration)
            => TestStore.CreateCollection<TKey, Record<TKey>>(
                TestStore.AdjustCollectionName($"key-type-{typeof(TKey).Name}"),
                CreateRecordDefinition<TKey>(withAutoGeneration));

        public override VectorStoreCollection<object, Dictionary<string, object?>> CreateDynamicCollection<TKey>(bool withAutoGeneration)
            => TestStore.CreateDynamicCollection(
                TestStore.AdjustCollectionName($"key-type-{typeof(TKey).Name}"),
                CreateRecordDefinition<TKey>(withAutoGeneration));
    }
}
