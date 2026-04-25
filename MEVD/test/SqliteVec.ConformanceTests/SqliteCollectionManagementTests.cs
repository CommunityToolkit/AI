// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SqliteVec.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace SqliteVec.ConformanceTests;

public class SqliteCollectionManagementTests(SqliteFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<SqliteFixture>
{
}
