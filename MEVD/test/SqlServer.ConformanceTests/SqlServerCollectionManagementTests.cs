// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SqlServer.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace SqlServer.ConformanceTests;

public class SqlServerCollectionManagementTests(SqlServerCollectionManagementTests.Fixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<SqlServerCollectionManagementTests.Fixture>
{
    public class Fixture : VectorStoreFixture
    {
        public override TestStore TestStore => SqlServerTestStore.Instance;
    }
}
