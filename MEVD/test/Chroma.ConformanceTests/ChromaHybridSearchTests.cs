// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Chroma.ConformanceTests;

/// <summary>
/// Hybrid search needs the Search API and the sparse vector indexes of Chroma, which distributed Chroma, the one Chroma Cloud runs on, and the
/// servers that declare sparse vector indexes have: these tests run only against such a server, and are skipped otherwise. See the README for the settings.
/// </summary>
public class ChromaHybridSearchTests(ChromaHybridSearchTests.VectorAndStringFixture vectorAndStringFixture, ChromaHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<string>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<ChromaHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<ChromaHybridSearchTests.MultiTextFixture>
{
    private const string SkipReason = "Hybrid search needs sparse vector indexes: set Chroma:ConnectionString to distributed Chroma, the one Chroma Cloud runs on, like https://api.trychroma.com or an address with Distributed=true, or to a server that declares sparse vector indexes, to run it.";

    public override Task HybridSearchAsync()
    {
        Assert.SkipUnless(ChromaTestStore.Instance.SupportsSparseVectorIndexes, SkipReason);
        return base.HybridSearchAsync();
    }

    public override Task HybridSearchAsync_with_filter()
    {
        Assert.SkipUnless(ChromaTestStore.Instance.SupportsSparseVectorIndexes, SkipReason);
        return base.HybridSearchAsync_with_filter();
    }

    public override Task HybridSearchAsync_with_top()
    {
        Assert.SkipUnless(ChromaTestStore.Instance.SupportsSparseVectorIndexes, SkipReason);
        return base.HybridSearchAsync_with_top();
    }

    public override Task HybridSearchAsync_with_Skip()
    {
        Assert.SkipUnless(ChromaTestStore.Instance.SupportsSparseVectorIndexes, SkipReason);
        return base.HybridSearchAsync_with_Skip();
    }

    public override Task HybridSearchAsync_with_multiple_keywords_ranks_matched_keywords_higher()
    {
        Assert.SkipUnless(ChromaTestStore.Instance.SupportsSparseVectorIndexes, SkipReason);
        return base.HybridSearchAsync_with_multiple_keywords_ranks_matched_keywords_higher();
    }

    public override Task HybridSearchAsync_with_multiple_text_properties()
    {
        Assert.SkipUnless(ChromaTestStore.Instance.SupportsSparseVectorIndexes, SkipReason);
        return base.HybridSearchAsync_with_multiple_text_properties();
    }

    public override Task HybridSearchAsync_without_explicitly_specified_property_fails()
    {
        Assert.SkipUnless(ChromaTestStore.Instance.SupportsSparseVectorIndexes, SkipReason);
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
