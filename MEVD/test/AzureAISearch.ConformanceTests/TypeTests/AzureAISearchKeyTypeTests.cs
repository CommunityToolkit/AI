// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureAISearch.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace AzureAISearch.ConformanceTests.TypeTests;

public class AzureAISearchKeyTypeTests(AzureAISearchKeyTypeTests.Fixture fixture)
    : KeyTypeTests(fixture), IClassFixture<AzureAISearchKeyTypeTests.Fixture>
{
    [Fact]
    public virtual Task String() => this.Test<string>("foo", "bar");

    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => AzureAISearchTestStore.Instance;
    }
}
