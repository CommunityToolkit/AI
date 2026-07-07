// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Cosmos.ConformanceTests;

public sealed class CosmosNoSqlDynamicModelTests(CosmosNoSqlDynamicModelTests.Fixture fixture)
    : DynamicModelTests<string>(fixture), IClassFixture<CosmosNoSqlDynamicModelTests.Fixture>
{
    public override Task SearchAsync_with_Skip()
    {
        // The vNext emulator's DiskANN index does not support OFFSET in vector search.
        if (!CosmosNoSqlTestStore.Instance.UsesLocalEmulator)
        {
            return base.SearchAsync_with_Skip();
        }

        return Task.CompletedTask;
    }

    public new sealed class Fixture : DynamicModelTests<string>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;
    }
}
