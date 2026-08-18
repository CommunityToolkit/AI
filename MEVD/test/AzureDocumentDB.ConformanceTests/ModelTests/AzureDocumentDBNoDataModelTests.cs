// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureDocumentDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureDocumentDB.ConformanceTests.ModelTests;

public class AzureDocumentDBNoDataModelTests(AzureDocumentDBNoDataModelTests.Fixture fixture)
    : NoDataModelTests<string>(fixture), IClassFixture<AzureDocumentDBNoDataModelTests.Fixture>
{
    public new class Fixture : NoDataModelTests<string>.Fixture
    {
        public override TestStore TestStore => AzureDocumentDBTestStore.Instance;
    }
}
