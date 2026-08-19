// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

// The type is internal to disable the tests due to emulator limitations
internal sealed class CosmosDBHybridSearchTests(
    CosmosDBHybridSearchTests.VectorAndStringFixture vectorAndStringFixture,
    CosmosDBHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<string>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<CosmosDBHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<CosmosDBHybridSearchTests.MultiTextFixture>
{
    public new sealed class VectorAndStringFixture : HybridSearchTests<string>.VectorAndStringFixture
    {
        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(VectorAndStringFixture)));

        public override TestStore TestStore => _store.Value;
    }

    public new sealed class MultiTextFixture : HybridSearchTests<string>.MultiTextFixture
    {
        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(MultiTextFixture)));

        public override TestStore TestStore => _store.Value;
    }
}
