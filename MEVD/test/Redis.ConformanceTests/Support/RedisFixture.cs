// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace Redis.ConformanceTests.Support;

public class RedisFixture : VectorStoreFixture
{
    public override TestStore TestStore => RedisTestStore.JsonInstance;
}
