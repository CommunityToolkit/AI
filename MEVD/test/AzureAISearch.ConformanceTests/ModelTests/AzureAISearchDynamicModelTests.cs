// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureAISearch.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureAISearch.ConformanceTests.ModelTests;

public class AzureAISearchDynamicModelTests(AzureAISearchDynamicModelTests.Fixture fixture)
    : DynamicModelTests<string>(fixture), IClassFixture<AzureAISearchDynamicModelTests.Fixture>
{
    public new class Fixture : DynamicModelTests<string>.Fixture
    {
        public override TestStore TestStore => AzureAISearchTestStore.Instance;
    }
}
