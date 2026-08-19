// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace AzureCosmosDB.ConformanceTests.Support;

public sealed class CosmosFixture : VectorStoreFixture
{
    private readonly Lazy<CosmosTestStore> _store = new(() => new CosmosTestStore(nameof(CosmosFixture)));

    public override TestStore TestStore => _store.Value;
}
