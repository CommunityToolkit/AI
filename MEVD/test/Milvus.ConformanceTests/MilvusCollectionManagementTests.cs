// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace Milvus.ConformanceTests;

public class MilvusCollectionManagementTests(MilvusFixture fixture)
    : CollectionManagementTests<long>(fixture), IClassFixture<MilvusFixture>
{
}

