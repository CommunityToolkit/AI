// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests;
using VectorData.ConformanceTests.ModelTests;

namespace SqliteVec.ConformanceTests;

public class SqliteTestSuiteImplementationTests : TestSuiteImplementationTests
{
    protected override ICollection<Type> IgnoredTestBases { get; } =
    [
        typeof(DynamicModelTests<>),

        // Hybrid search not supported
        typeof(HybridSearchTests<>)
    ];
}
