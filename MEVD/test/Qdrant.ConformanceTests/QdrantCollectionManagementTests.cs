// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Qdrant.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace Qdrant.ConformanceTests;

public class QdrantCollectionManagementTests_NamedVectors(QdrantNamedVectorsFixture fixture)
    : CollectionManagementTests<ulong>(fixture), IClassFixture<QdrantNamedVectorsFixture>
{
}

public class QdrantCollectionManagementTests_UnnamedVector(QdrantUnnamedVectorFixture fixture)
    : CollectionManagementTests<ulong>(fixture), IClassFixture<QdrantUnnamedVectorFixture>
{
}
