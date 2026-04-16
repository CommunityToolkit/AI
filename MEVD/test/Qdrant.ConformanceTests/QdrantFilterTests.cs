// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Qdrant.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Qdrant.ConformanceTests;

public class QdrantFilterTests(QdrantFilterTests.Fixture fixture)
    : FilterTests<ulong>(fixture), IClassFixture<QdrantFilterTests.Fixture>
{
    public new class Fixture : FilterTests<ulong>.Fixture
    {
        public override TestStore TestStore => QdrantTestStore.NamedVectorsInstance;
    }
}
