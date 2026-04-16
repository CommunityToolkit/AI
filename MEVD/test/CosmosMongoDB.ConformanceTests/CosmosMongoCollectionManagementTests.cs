// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosMongoDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace CosmosMongoDB.ConformanceTests;

public class CosmosMongoCollectionManagementTests(CosmosMongoFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<CosmosMongoFixture>
{
}
