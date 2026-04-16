// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Weaviate.ConformanceTests.Support;
using Xunit;

namespace Weaviate.ConformanceTests;

public class WeaviateIndexKindTests(WeaviateIndexKindTests.Fixture fixture)
    : IndexKindTests<Guid>(fixture), IClassFixture<WeaviateIndexKindTests.Fixture>
{
    [Fact]
    public virtual Task Hnsw()
        => this.Test(IndexKind.Hnsw);

    // The dynamic index requires the ASYNC_INDEXING Weaviate server environment variable, which decouples indexing
    // from object creation. This causes eventual consistency issues for other tests (e.g. SearchAsync_with_Filter),
    // so we can't enable it in the test container.

    public new class Fixture() : IndexKindTests<Guid>.Fixture
    {
        public override TestStore TestStore => WeaviateTestStore.NamedVectorsInstance;
    }
}
