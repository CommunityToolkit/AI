// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Cosmos.ConformanceTests;

// The type is internal to disable the tests due to emulator limitations
internal sealed class CosmosNoSqlHybridSearchTests(
    CosmosNoSqlHybridSearchTests.VectorAndStringFixture vectorAndStringFixture,
    CosmosNoSqlHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<string>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<CosmosNoSqlHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<CosmosNoSqlHybridSearchTests.MultiTextFixture>
{
    public new sealed class VectorAndStringFixture : HybridSearchTests<string>.VectorAndStringFixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(VectorAndStringFixture)));

        public override TestStore TestStore => _store.Value;
    }

    public new sealed class MultiTextFixture : HybridSearchTests<string>.MultiTextFixture
    {
        private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(MultiTextFixture)));

        public override TestStore TestStore => _store.Value;
    }
}
