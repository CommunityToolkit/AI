// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

public sealed class CosmosCollectionManagementTests(CosmosFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<CosmosFixture>
{
}
