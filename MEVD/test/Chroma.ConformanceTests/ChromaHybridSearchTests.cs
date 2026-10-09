// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Chroma.ConformanceTests;

/// <summary>
/// Hybrid search needs the Search API and the sparse vector indexes of Chroma, which only Chroma Cloud has:
/// these tests run only when Chroma:ConnectionString has a Chroma Cloud address, like https://api.trychroma.com, and are skipped otherwise. See the README for the settings.
/// </summary>
public class ChromaHybridSearchTests(ChromaHybridSearchTests.VectorAndStringFixture vectorAndStringFixture, ChromaHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<string>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<ChromaHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<ChromaHybridSearchTests.MultiTextFixture>
{
    private const string SkipReason = "Hybrid search needs Chroma Cloud: set Chroma:ConnectionString to a Chroma Cloud address, like https://api.trychroma.com to run it.";

    public override Task HybridSearchAsync()
    {
        Assert.SkipUnless(ChromaTestStore.IsChromaCloud, SkipReason);
        return base.HybridSearchAsync();
    }

    public override Task HybridSearchAsync_with_filter()
    {
        Assert.SkipUnless(ChromaTestStore.IsChromaCloud, SkipReason);
        return base.HybridSearchAsync_with_filter();
    }

    public override Task HybridSearchAsync_with_top()
    {
        Assert.SkipUnless(ChromaTestStore.IsChromaCloud, SkipReason);
        return base.HybridSearchAsync_with_top();
    }

    public override Task HybridSearchAsync_with_Skip()
    {
        Assert.SkipUnless(ChromaTestStore.IsChromaCloud, SkipReason);
        return base.HybridSearchAsync_with_Skip();
    }

    public override Task HybridSearchAsync_with_multiple_keywords_ranks_matched_keywords_higher()
    {
        Assert.SkipUnless(ChromaTestStore.IsChromaCloud, SkipReason);
        return base.HybridSearchAsync_with_multiple_keywords_ranks_matched_keywords_higher();
    }

    public override Task HybridSearchAsync_with_multiple_text_properties()
    {
        Assert.SkipUnless(ChromaTestStore.IsChromaCloud, SkipReason);
        return base.HybridSearchAsync_with_multiple_text_properties();
    }

    public override Task HybridSearchAsync_without_explicitly_specified_property_fails()
    {
        Assert.SkipUnless(ChromaTestStore.IsChromaCloud, SkipReason);
        return base.HybridSearchAsync_without_explicitly_specified_property_fails();
    }

    public new class VectorAndStringFixture : HybridSearchTests<string>.VectorAndStringFixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }

    public new class MultiTextFixture : HybridSearchTests<string>.MultiTextFixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }
}
