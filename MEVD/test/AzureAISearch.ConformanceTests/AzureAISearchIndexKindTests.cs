// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureAISearch.ConformanceTests.Support;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureAISearch.ConformanceTests;

public class AzureAISearchIndexKindTests(AzureAISearchIndexKindTests.Fixture fixture)
    : IndexKindTests<string>(fixture), IClassFixture<AzureAISearchIndexKindTests.Fixture>
{
    [Fact]
    public virtual Task Hnsw()
        => this.Test(IndexKind.Hnsw);

    public new class Fixture() : IndexKindTests<string>.Fixture
    {
        public override TestStore TestStore => AzureAISearchTestStore.Instance;
    }
}
