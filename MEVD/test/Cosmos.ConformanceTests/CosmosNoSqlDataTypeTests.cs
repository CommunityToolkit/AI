// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Cosmos.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace Cosmos.ConformanceTests;

// The type is internal to disable the tests due to emulator limitations
internal sealed class CosmosNoSqlDataTypeTests(CosmosNoSqlDataTypeTests.Fixture fixture)
    : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>(fixture), IClassFixture<CosmosNoSqlDataTypeTests.Fixture>
{
    public override Task DateTimeOffset()
        => Task.CompletedTask;

    public new sealed class Fixture : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>.Fixture
    {
        public override TestStore TestStore => CosmosNoSqlTestStore.Instance;

        public override Type[] UnsupportedDefaultTypes { get; } =
        [
            typeof(byte),
            typeof(short),
            typeof(decimal),
            typeof(Guid),
            typeof(TimeOnly)
        ];
    }
}
