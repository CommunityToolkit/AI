// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.VectorData.Cosmos;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace CommunityToolkit.VectorData.Cosmos.UnitTests;

public sealed class CosmosNoSqlOptionsTests
{
    [Fact]
    public void CollectionOptionsPropertiesCanBeConfigured()
    {
        JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);
        CosmosNoSqlCollectionOptions source = new()
        {
            JsonSerializerOptions = serializerOptions,
            PartitionKeyProperties = new[] { "tenantId", "category" },
            Automatic = false,
            IndexingMode = IndexingMode.None,
        };

        Assert.Same(serializerOptions, source.JsonSerializerOptions);
        Assert.Equal(
            new List<string> { "tenantId", "category" },
            new List<string>(source.PartitionKeyProperties!));
        Assert.False(source.Automatic);
        Assert.Equal(IndexingMode.None, source.IndexingMode);
    }

    [Fact]
    public void VectorStoreOptionsPropertiesCanBeConfigured()
    {
        JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);
        CosmosNoSqlVectorStoreOptions source = new()
        {
            JsonSerializerOptions = serializerOptions,
        };

        Assert.Same(serializerOptions, source.JsonSerializerOptions);
    }
}
