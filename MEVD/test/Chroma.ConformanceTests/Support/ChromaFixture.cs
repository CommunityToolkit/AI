// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using VectorData.ConformanceTests.Support;

namespace Chroma.ConformanceTests.Support;

public class ChromaFixture : VectorStoreFixture
{
    public override TestStore TestStore => ChromaTestStore.Instance;
}
