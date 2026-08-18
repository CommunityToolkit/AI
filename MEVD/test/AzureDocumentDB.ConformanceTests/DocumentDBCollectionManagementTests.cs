// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureDocumentDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace AzureDocumentDB.ConformanceTests;

public class DocumentDBCollectionManagementTests(DocumentDBFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<DocumentDBFixture>
{
}
