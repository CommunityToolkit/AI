// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests.Support;
using VectorData.ConformanceTests.TypeTests;
using Xunit;

namespace Chroma.ConformanceTests.TypeTests;

public class ChromaDataTypeTests(ChromaDataTypeTests.Fixture fixture)
    : DataTypeTests<Guid, DataTypeTests<Guid>.DefaultRecord>(fixture), IClassFixture<ChromaDataTypeTests.Fixture>
{
    // Chroma does not compare an array field with an array
    [Fact]
    public override Task String_array()
        => this.Test<string[]>(
            "StringArray",
            ["foo", "bar"],
            ["foo", "baz"],
            isFilterable: false);

    public new class Fixture : DataTypeTests<Guid, DataTypeTests<Guid>.DefaultRecord>.Fixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;

        // Chroma metadata has no null values, and Chroma has no filter for a missing field
        public override bool IsNullFilteringSupported => false;

        public override Type[] UnsupportedDefaultTypes { get; } =
        [
            typeof(byte),
            typeof(short),
            typeof(decimal),
            typeof(Guid),
#if NET
            typeof(TimeOnly)
#endif
        ];
    }
}
