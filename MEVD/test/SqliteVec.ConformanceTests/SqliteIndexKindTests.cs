// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SqliteVec.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace SqliteVec.ConformanceTests;

public class SqliteIndexKindTests(SqliteIndexKindTests.Fixture fixture)
    : IndexKindTests<int>(fixture), IClassFixture<SqliteIndexKindTests.Fixture>
{
    public new class Fixture() : IndexKindTests<int>.Fixture
    {
        public override TestStore TestStore => SqliteTestStore.Instance;
    }
}
