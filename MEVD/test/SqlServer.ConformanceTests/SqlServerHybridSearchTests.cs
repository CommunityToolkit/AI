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
        IClassFixture<SqlServerHybridSearchTests.MultiTextFixture>,
        IAsyncLifetime
{
    async ValueTask IAsyncLifetime.InitializeAsync()
        => await AzureSqlHelper.SkipUnlessAzureSqlAsync();

    ValueTask IAsyncDisposable.DisposeAsync() => default;

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
