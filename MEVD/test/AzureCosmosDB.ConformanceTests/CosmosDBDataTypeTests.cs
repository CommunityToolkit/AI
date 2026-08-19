// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureCosmosDB.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace AzureCosmosDB.ConformanceTests;

// The type is internal to disable the tests due to emulator limitations
internal sealed class CosmosDBDataTypeTests(CosmosDBDataTypeTests.Fixture fixture)
    : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>(fixture), IClassFixture<CosmosDBDataTypeTests.Fixture>
{
    public override Task DateTimeOffset()
        => Task.CompletedTask;

    public new sealed class Fixture : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>.Fixture
    {
        private readonly Lazy<CosmosDBTestStore> _store = new(() => new CosmosDBTestStore(nameof(CosmosDBDataTypeTests)));

        public override TestStore TestStore => _store.Value;

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
