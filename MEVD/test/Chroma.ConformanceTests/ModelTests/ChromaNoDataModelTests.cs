// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Chroma.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Chroma.ConformanceTests.ModelTests;

public class ChromaNoDataModelTests(ChromaNoDataModelTests.Fixture fixture)
    : NoDataModelTests<string>(fixture), IClassFixture<ChromaNoDataModelTests.Fixture>
{
    public new class Fixture : NoDataModelTests<string>.Fixture
    {
        public override TestStore TestStore => ChromaTestStore.Instance;
    }
}
