// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureDocumentDB.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureDocumentDB.ConformanceTests.ModelTests;

public class DocumentDBMultiVectorModelTests(DocumentDBMultiVectorModelTests.Fixture fixture)
    : MultiVectorModelTests<string>(fixture), IClassFixture<DocumentDBMultiVectorModelTests.Fixture>
{
    public new class Fixture : MultiVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => DocumentDBTestStore.Instance;
    }
}
