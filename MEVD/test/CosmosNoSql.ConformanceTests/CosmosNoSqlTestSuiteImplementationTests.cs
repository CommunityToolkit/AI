// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests;
using VectorData.ConformanceTests.TypeTests;

namespace CosmosNoSql.ConformanceTests;

public sealed class CosmosNoSqlTestSuiteImplementationTests : TestSuiteImplementationTests
{
    protected override ICollection<Type> IgnoredTestBases { get; } =
    [
        // Not supported by current emulator version
        typeof(FilterTests<>),
        typeof(DataTypeTests<>),
        typeof(DataTypeTests<,>),
        typeof(HybridSearchTests<>),
    ];
}
