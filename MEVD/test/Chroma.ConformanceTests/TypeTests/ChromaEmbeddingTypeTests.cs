// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

#pragma warning disable CA2000 // Dispose objects before losing scope

namespace Chroma.ConformanceTests.TypeTests;

public class ChromaEmbeddingTypeTests(ChromaEmbeddingTypeTests.Fixture fixture)
    : EmbeddingTypeTests<Guid>(fixture), IClassFixture<ChromaEmbeddingTypeTests.Fixture>
{
    public new class Fixture : EmbeddingTypeTests<Guid>.Fixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }
}
