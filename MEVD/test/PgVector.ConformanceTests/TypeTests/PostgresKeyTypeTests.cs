// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using PgVector.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace PgVector.ConformanceTests.TypeTests;

public class PostgresKeyTypeTests(PostgresKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<PostgresKeyTypeTests.Fixture>
{
    [Fact]
    public virtual Task Int() => this.Test<int>(8, supportsAutoGeneration: true);

    [Fact]
    public virtual Task Long() => this.Test<long>(8L, supportsAutoGeneration: true);

    [Fact]
    public virtual Task String() => this.Test<string>("foo", "bar");

    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => PostgresTestStore.Instance;
    }
}
