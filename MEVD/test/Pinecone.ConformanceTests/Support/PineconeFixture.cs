// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace Pinecone.ConformanceTests.Support;

public class PineconeFixture : VectorStoreFixture
{
    public override TestStore TestStore => PineconeTestStore.Instance;
}
