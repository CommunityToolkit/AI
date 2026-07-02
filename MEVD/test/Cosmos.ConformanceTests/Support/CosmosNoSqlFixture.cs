// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace Cosmos.ConformanceTests.Support;

public sealed class CosmosNoSqlFixture : VectorStoreFixture
{
    public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
}
