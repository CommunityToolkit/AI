// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests.ModelTests;

public class MilvusMultiVectorModelTests(MilvusMultiVectorModelTests.Fixture fixture)
    : MultiVectorModelTests<long>(fixture), IClassFixture<MilvusMultiVectorModelTests.Fixture>
{
    public new class Fixture : MultiVectorModelTests<long>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}
