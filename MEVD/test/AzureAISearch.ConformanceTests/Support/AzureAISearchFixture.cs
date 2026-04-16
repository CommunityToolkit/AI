// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace AzureAISearch.ConformanceTests.Support;

public class AzureAISearchFixture : VectorStoreFixture
{
    public override TestStore TestStore => AzureAISearchTestStore.Instance;
}
