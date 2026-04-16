// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureAISearch.ConformanceTests.Support;
using VectorData.ConformanceTests;
using Xunit;

namespace AzureAISearch.ConformanceTests;

public class AzureAISearchCollectionManagementTests(AzureAISearchFixture fixture)
    : CollectionManagementTests<string>(fixture), IClassFixture<AzureAISearchFixture>;
