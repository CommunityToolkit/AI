// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace AzureDocumentDB.ConformanceTests.Support;

public class AzureDocumentDBFixture : VectorStoreFixture
{
    public override TestStore TestStore => AzureDocumentDBTestStore.Instance;
}
