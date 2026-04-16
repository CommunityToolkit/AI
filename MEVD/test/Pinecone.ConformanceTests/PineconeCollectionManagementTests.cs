// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Pinecone.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace Pinecone.ConformanceTests;

public class PineconeCollectionManagementTests(PineconeFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<PineconeFixture>;
