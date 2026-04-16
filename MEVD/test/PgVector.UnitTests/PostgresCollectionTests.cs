// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.VectorData;
using CommunityToolkit.VectorData.PgVector;
using Xunit;

namespace SemanticKernel.Connectors.PgVector.UnitTests;

public class PostgresCollectionTests
{
    private const string TestCollectionName = "testcollection";

    [Fact]
    public void ThrowsForUnsupportedType()
    {
        // Arrange
        var recordDefinition = new VectorStoreCollectionDefinition
        {
            Properties = [
                new VectorStoreKeyProperty("HotelId", typeof(ulong)),
                new VectorStoreDataProperty("HotelName", typeof(string)) { IsIndexed = true, IsFullTextIndexed = true },
            ]
        };
        var options = new PostgresCollectionOptions()
        {
            Definition = recordDefinition
        };

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => new PostgresDynamicCollection("Host=localhost;Database=test;", TestCollectionName, options));
    }
}
