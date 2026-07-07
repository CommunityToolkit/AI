// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosNoSql.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlCollectionManagementTests(CosmosNoSqlFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<CosmosNoSqlFixture>
{
}
