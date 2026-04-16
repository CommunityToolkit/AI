// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Qdrant.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Qdrant.ConformanceTests.ModelTests;

public class QdrantNoVectorModelTests_NamedVectors(QdrantNoVectorModelTests_NamedVectors.Fixture fixture)
    : NoVectorModelTests<ulong>(fixture), IClassFixture<QdrantNoVectorModelTests_NamedVectors.Fixture>
{
    public new class Fixture : NoVectorModelTests<ulong>.Fixture
    {
        public override TestStore TestStore => QdrantTestStore.NamedVectorsInstance;
    }
}
