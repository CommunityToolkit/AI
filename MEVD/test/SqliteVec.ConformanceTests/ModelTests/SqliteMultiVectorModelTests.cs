// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SqliteVec.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace SqliteVec.ConformanceTests.ModelTests;

public class SqliteMultiVectorModelTests(SqliteMultiVectorModelTests.Fixture fixture)
    : MultiVectorModelTests<string>(fixture), IClassFixture<SqliteMultiVectorModelTests.Fixture>
{
    public new class Fixture : MultiVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => SqliteTestStore.Instance;
    }
}
