// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureDocumentDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureDocumentDB.ConformanceTests.ModelTests;

public class DocumentDBBasicModelTests(DocumentDBBasicModelTests.Fixture fixture)
    : BasicModelTests<string>(fixture), IClassFixture<DocumentDBBasicModelTests.Fixture>
{
    public new class Fixture : BasicModelTests<string>.Fixture
    {
        public override TestStore TestStore => DocumentDBTestStore.Instance;
    }
}
