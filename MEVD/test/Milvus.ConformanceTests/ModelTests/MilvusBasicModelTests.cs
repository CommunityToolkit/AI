// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests.ModelTests;

public class MilvusBasicModelTests(MilvusBasicModelTests.Fixture fixture)
    : BasicModelTests<string>(fixture), IClassFixture<MilvusBasicModelTests.Fixture>
{
    public override async Task GetAsync_with_filter_and_OrderBy()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(base.GetAsync_with_filter_and_OrderBy);
        Assert.Equal("Milvus does not support ordering.", exception.Message);
    }

    public override async Task GetAsync_with_filter_and_OrderBy_and_Skip()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(base.GetAsync_with_filter_and_OrderBy_and_Skip);
        Assert.Equal("Milvus does not support ordering.", exception.Message);
    }

    public override async Task GetAsync_with_filter_and_multiple_OrderBys()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(base.GetAsync_with_filter_and_multiple_OrderBys);
        Assert.Equal("Milvus does not support ordering.", exception.Message);
    }

    public new class Fixture : BasicModelTests<string>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}
