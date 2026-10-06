// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace Chroma.ConformanceTests.TypeTests;

public class ChromaKeyTypeTests(ChromaKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<ChromaKeyTypeTests.Fixture>
{
    [Fact]
    public virtual Task String() => this.Test<string>("foo", "bar");

    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }
}
