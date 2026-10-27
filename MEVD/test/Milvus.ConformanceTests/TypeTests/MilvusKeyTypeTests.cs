// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests.TypeTests;

public class MilvusKeyTypeTests(MilvusKeyTypeTests.Fixture fixture) : KeyTypeTests(fixture), IClassFixture<MilvusKeyTypeTests.Fixture>
{
    [Fact] public Task Long() => Test<long>(42L);
    [Fact] public Task String() => Test<string>("one", "two");
    [Fact] public Task String_with_non_BMP_characters() => Test<string>("emoji \U0001F600", "emoji \U0001F680");
    [Fact] public Task String_with_escape_characters() => Test<string>("a\"b\\c\n", "d\te\rf");
    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;
    }
}

