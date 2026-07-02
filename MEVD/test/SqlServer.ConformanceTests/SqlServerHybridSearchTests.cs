// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SqlServer.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace SqlServer.ConformanceTests;

public class SqlServerHybridSearchTests(
    SqlServerHybridSearchTests.VectorAndStringFixture vectorAndStringFixture,
    SqlServerHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<int>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<SqlServerHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<SqlServerHybridSearchTests.MultiTextFixture>
{
    public new class VectorAndStringFixture : HybridSearchTests<int>.VectorAndStringFixture
    {
        public override TestStore TestStore => SqlServerTestStore.Instance;
    }

    public new class MultiTextFixture : HybridSearchTests<int>.MultiTextFixture
    {
        public override TestStore TestStore => SqlServerTestStore.Instance;
    }
}
