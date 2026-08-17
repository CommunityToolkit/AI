// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CosmosDocumentDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace CosmosDocumentDB.ConformanceTests.ModelTests;

public class CosmosDocumentDBNoDataModelTests(CosmosDocumentDBNoDataModelTests.Fixture fixture)
    : NoDataModelTests<string>(fixture), IClassFixture<CosmosDocumentDBNoDataModelTests.Fixture>
{
    public new class Fixture : NoDataModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosDocumentDBTestStore.Instance;
    }
}
