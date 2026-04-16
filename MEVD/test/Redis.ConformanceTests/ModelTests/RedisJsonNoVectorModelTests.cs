// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Redis.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Redis.ConformanceTests.ModelTests;

public class RedisJsonNoVectorModelTests(RedisJsonNoVectorModelTests.Fixture fixture)
    : NoVectorModelTests<string>(fixture), IClassFixture<RedisJsonNoVectorModelTests.Fixture>
{
    public new class Fixture : NoVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => RedisTestStore.JsonInstance;
    }
}
