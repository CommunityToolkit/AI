// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests;
using VectorData.ConformanceTests.ModelTests;

namespace Pinecone.ConformanceTests;

public class PineconeTestSuiteImplementationTests : TestSuiteImplementationTests
{
    protected override ICollection<Type> IgnoredTestBases { get; } =
    [
        // Pinecone does not support multiple vectors
        typeof(MultiVectorModelTests<>),

        // Hybrid search not supported
        typeof(HybridSearchTests<>),

        // Pinecone requires at least one vector property; records without vectors are not supported.
        typeof(NoVectorModelTests<>),
    ];
}
