// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Weaviate.ConformanceTests.Support;
using Xunit;

namespace Weaviate.ConformanceTests;

public class WeaviateCollectionManagementTests_NamedVectors(WeaviateCollectionManagementTests_NamedVectors.Fixture fixture)
    : CollectionManagementTests<Guid>(fixture), IClassFixture<WeaviateCollectionManagementTests_NamedVectors.Fixture>
{
    public class Fixture : VectorStoreFixture
    {
        public override TestStore TestStore => WeaviateTestStore.NamedVectorsInstance;
    }
}

public class WeaviateCollectionManagementTests_UnnamedVector(WeaviateCollectionManagementTests_UnnamedVector.Fixture fixture)
    : CollectionManagementTests<Guid>(fixture), IClassFixture<WeaviateCollectionManagementTests_UnnamedVector.Fixture>
{
    public class Fixture : VectorStoreFixture
    {
        public override TestStore TestStore => WeaviateTestStore.UnnamedVectorInstance;
    }
}
