// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Weaviate.ConformanceTests.Support;
using Xunit;

namespace Weaviate.ConformanceTests.TypeTests;

public class WeaviateKeyTypeTests(WeaviateKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<WeaviateKeyTypeTests.Fixture>
{
    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => WeaviateTestStore.NamedVectorsInstance;
    }
}
