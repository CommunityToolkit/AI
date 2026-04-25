// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace Qdrant.ConformanceTests.Support;

public class QdrantUnnamedVectorFixture : VectorStoreFixture
{
    public override TestStore TestStore => QdrantTestStore.UnnamedVectorInstance;
}
