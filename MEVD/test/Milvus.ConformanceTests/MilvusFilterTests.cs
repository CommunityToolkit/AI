// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests;

public class MilvusFilterTests(MilvusFilterTests.Fixture fixture) : FilterTests<string>(fixture), IClassFixture<MilvusFilterTests.Fixture>
{
    public new class Fixture : FilterTests<string>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}
