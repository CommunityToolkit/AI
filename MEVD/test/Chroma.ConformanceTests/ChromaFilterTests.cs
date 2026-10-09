// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Chroma.ConformanceTests;

public class ChromaFilterTests(ChromaFilterTests.Fixture fixture)
    : FilterTests<string>(fixture), IClassFixture<ChromaFilterTests.Fixture>
{
    #region Null checking

    // Chroma metadata has no null values, and Chroma has no filter for a missing field
    public override Task Equal_with_null_reference_type()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Equal_with_null_reference_type());

    public override Task Equal_with_null_captured()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Equal_with_null_captured());

    public override Task NotEqual_with_null_reference_type()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.NotEqual_with_null_reference_type());

    public override Task NotEqual_with_null_captured()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.NotEqual_with_null_captured());

    #endregion

    public new class Fixture : FilterTests<string>.Fixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }
}
