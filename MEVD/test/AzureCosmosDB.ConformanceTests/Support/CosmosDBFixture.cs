// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace AzureCosmosDB.ConformanceTests.Support;

public sealed class CosmosDBFixture : VectorStoreFixture
{
    private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBFixture)));

    public override TestStore TestStore => _store.Value;
}
