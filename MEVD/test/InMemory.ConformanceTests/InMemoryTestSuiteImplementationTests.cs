// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests;

namespace InMemory.ConformanceTests;

public class InMemoryTestSuiteImplementationTests : TestSuiteImplementationTests
{
    protected override ICollection<Type> IgnoredTestBases { get; } =
    [
        typeof(DependencyInjectionTests<,,,>),
        typeof(DependencyInjectionTests<>),

        // Hybrid search not supported
        typeof(HybridSearchTests<>)
    ];
}
