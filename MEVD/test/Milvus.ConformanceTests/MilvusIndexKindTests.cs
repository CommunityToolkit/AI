// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;
namespace Milvus.ConformanceTests;
public class MilvusIndexKindTests(MilvusIndexKindTests.Fixture fixture) : IndexKindTests<string>(fixture), IClassFixture<MilvusIndexKindTests.Fixture>
{
    public new class Fixture : IndexKindTests<string>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}
