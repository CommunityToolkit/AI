// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests;

namespace PgVector.ConformanceTests;

public class PostgresTestSuiteImplementationTests : TestSuiteImplementationTests
{
    protected override ICollection<Type> IgnoredTestBases { get; } =
    [
        // Hybrid search not supported
        typeof(HybridSearchTests<>)
    ];
}
