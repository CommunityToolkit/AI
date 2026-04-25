// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Redis.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace Redis.ConformanceTests;

public class RedisHashSetCollectionManagementTests(RedisHashSetFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<RedisHashSetFixture>
{
}
