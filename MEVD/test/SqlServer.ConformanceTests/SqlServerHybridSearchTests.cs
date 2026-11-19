// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SqlServer.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace SqlServer.ConformanceTests;

// The HybridSearch tests require running against Azure SQL.
// The fixtures do not create their collections elsewhere, and each test is skipped.
public class SqlServerHybridSearchTests(
    SqlServerHybridSearchTests.VectorAndStringFixture vectorAndStringFixture,
    SqlServerHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<int>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<SqlServerHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<SqlServerHybridSearchTests.MultiTextFixture>
{
    public override async Task HybridSearchAsync()
    {
        await AzureSqlHelper.SkipUnlessAzureSqlAsync();
        await base.HybridSearchAsync();
    }

    public override async Task HybridSearchAsync_with_filter()
    {
        await AzureSqlHelper.SkipUnlessAzureSqlAsync();
        await base.HybridSearchAsync_with_filter();
    }

    public override async Task HybridSearchAsync_with_top()
    {
        await AzureSqlHelper.SkipUnlessAzureSqlAsync();
        await base.HybridSearchAsync_with_top();
    }

    public override async Task HybridSearchAsync_with_Skip()
    {
        await AzureSqlHelper.SkipUnlessAzureSqlAsync();
        await base.HybridSearchAsync_with_Skip();
    }

    public override async Task HybridSearchAsync_with_multiple_keywords_ranks_matched_keywords_higher()
    {
        await AzureSqlHelper.SkipUnlessAzureSqlAsync();
        await base.HybridSearchAsync_with_multiple_keywords_ranks_matched_keywords_higher();
    }

    public override async Task HybridSearchAsync_with_multiple_text_properties()
    {
        await AzureSqlHelper.SkipUnlessAzureSqlAsync();
        await base.HybridSearchAsync_with_multiple_text_properties();
    }

    public override async Task HybridSearchAsync_without_explicitly_specified_property_fails()
    {
        await AzureSqlHelper.SkipUnlessAzureSqlAsync();
        await base.HybridSearchAsync_without_explicitly_specified_property_fails();
    }

    public new class VectorAndStringFixture : HybridSearchTests<int>.VectorAndStringFixture
    {
        private bool _initialized;

        public override TestStore TestStore => SqlServerTestStore.Instance;

        public override async ValueTask InitializeAsync()
        {
            if (await AzureSqlHelper.GetIsAzureSqlAsync())
            {
                await base.InitializeAsync();
                this._initialized = true;
            }
        }

        public override ValueTask DisposeAsync()
            => this._initialized ? base.DisposeAsync() : default;
    }

    public new class MultiTextFixture : HybridSearchTests<int>.MultiTextFixture
    {
        private bool _initialized;

        public override TestStore TestStore => SqlServerTestStore.Instance;

        public override async ValueTask InitializeAsync()
        {
            if (await AzureSqlHelper.GetIsAzureSqlAsync())
            {
                await base.InitializeAsync();
                this._initialized = true;
            }
        }

        public override ValueTask DisposeAsync()
            => this._initialized ? base.DisposeAsync() : default;
    }
}
