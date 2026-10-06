// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Chroma.ConformanceTests;

public class ChromaIndexKindTests(ChromaIndexKindTests.Fixture fixture)
    : IndexKindTests<string>(fixture), IClassFixture<ChromaIndexKindTests.Fixture>
{
    // Chroma does not support index-less searching
    public override Task Flat() => Assert.ThrowsAsync<NotSupportedException>(base.Flat);

    [Fact]
    public virtual Task Hnsw()
        => this.Test(IndexKind.Hnsw);

    public new class Fixture() : IndexKindTests<string>.Fixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }
}
