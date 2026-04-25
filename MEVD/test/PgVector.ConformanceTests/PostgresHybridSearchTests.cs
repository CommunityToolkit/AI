// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using PgVector.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace PgVector.ConformanceTests;

public class PostgresHybridSearchTests(
    PostgresHybridSearchTests.VectorAndStringFixture vectorAndStringFixture,
    PostgresHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<long>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<PostgresHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<PostgresHybridSearchTests.MultiTextFixture>
{
    public new class VectorAndStringFixture : HybridSearchTests<long>.VectorAndStringFixture
    {
        public override TestStore TestStore => PostgresTestStore.Instance;
    }

    public new class MultiTextFixture : HybridSearchTests<long>.MultiTextFixture
    {
        public override TestStore TestStore => PostgresTestStore.Instance;
    }
}
