// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Redis.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Redis.ConformanceTests;

public class RedisJsonIndexKindTests(RedisJsonIndexKindTests.Fixture fixture)
    : IndexKindTests<string>(fixture), IClassFixture<RedisJsonIndexKindTests.Fixture>
{
    public new class Fixture() : IndexKindTests<string>.Fixture
    {
        public override TestStore TestStore => RedisTestStore.JsonInstance;
    }
}
