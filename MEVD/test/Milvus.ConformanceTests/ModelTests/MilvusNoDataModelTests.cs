// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests.ModelTests;

public class MilvusNoDataModelTests(MilvusNoDataModelTests.Fixture fixture)
    : NoDataModelTests<long>(fixture), IClassFixture<MilvusNoDataModelTests.Fixture>
{
    public new class Fixture : NoDataModelTests<long>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}

