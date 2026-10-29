// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace Milvus.ConformanceTests.Support;

public sealed class MilvusFixture : VectorStoreFixture
{
    public override TestStore TestStore => MilvusTestStore.Instance;
}
