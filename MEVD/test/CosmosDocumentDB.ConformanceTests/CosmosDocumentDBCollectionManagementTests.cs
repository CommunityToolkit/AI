// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosDocumentDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace CosmosDocumentDB.ConformanceTests;

public class CosmosDocumentDBCollectionManagementTests(CosmosDocumentDBFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<CosmosDocumentDBFixture>
{
}
