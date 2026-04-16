// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using InMemory.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

#pragma warning disable CA2000 // Dispose objects before losing scope

namespace InMemory.ConformanceTests.TypeTests;

public class InMemoryEmbeddingTypeTests(InMemoryEmbeddingTypeTests.Fixture fixture)
    : EmbeddingTypeTests<Guid>(fixture), IClassFixture<InMemoryEmbeddingTypeTests.Fixture>
{
    public new class Fixture : EmbeddingTypeTests<Guid>.Fixture
    {
        public override TestStore TestStore => InMemoryTestStore.Instance;

        public override bool RecreateCollection => true;
        public override bool AssertNoVectorsLoadedWithEmbeddingGeneration => false;
    }
}
