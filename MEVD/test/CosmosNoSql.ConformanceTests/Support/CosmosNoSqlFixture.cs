// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace CosmosNoSql.ConformanceTests.Support;

public sealed class CosmosNoSqlFixture : VectorStoreFixture
{
    private readonly Lazy<CosmosNoSqlTestStore> _store = new(() => new CosmosNoSqlTestStore(nameof(CosmosNoSqlFixture)));

    public override TestStore TestStore => _store.Value;
}
