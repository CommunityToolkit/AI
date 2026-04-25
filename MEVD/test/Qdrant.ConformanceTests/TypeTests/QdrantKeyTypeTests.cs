// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Qdrant.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace Qdrant.ConformanceTests.TypeTests;

public class QdrantKeyTypeTests(QdrantKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<QdrantKeyTypeTests.Fixture>
{
    [Fact]
    public virtual Task ULong() => this.Test<ulong>(8UL);

    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => QdrantTestStore.NamedVectorsInstance;
    }
}
