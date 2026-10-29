// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests.TypeTests;

public class MilvusDataTypeTests(MilvusDataTypeTests.Fixture fixture) : DataTypeTests<Guid, DataTypeTests<Guid>.DefaultRecord>(fixture), IClassFixture<MilvusDataTypeTests.Fixture>
{
    [Fact]
    public override Task String_array() => Test<string[]>("StringArray", ["foo", "bar"], ["foo", "baz"], isFilterable: false);

    public new class Fixture : DataTypeTests<Guid, DataTypeTests<Guid>.DefaultRecord>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;

        public override Type[] UnsupportedDefaultTypes { get; } =
        [ typeof(byte), typeof(short), typeof(decimal), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset),
#if NET
          typeof(DateOnly), typeof(TimeOnly),
#endif
        ];
    }
}
