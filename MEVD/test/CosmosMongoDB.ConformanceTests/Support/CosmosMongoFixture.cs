// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace CosmosMongoDB.ConformanceTests.Support;

public class CosmosMongoFixture : VectorStoreFixture
{
    public override TestStore TestStore => CosmosMongoTestStore.Instance;
}
