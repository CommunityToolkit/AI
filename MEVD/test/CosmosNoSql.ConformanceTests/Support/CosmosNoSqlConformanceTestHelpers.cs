// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CosmosNoSql.ConformanceTests.Support;

internal static class CosmosNoSqlConformanceTestHelpers
{
    public static VectorStoreCollectionDefinition UseLowerCaseVectorStorageName(
        VectorStoreCollectionDefinition definition,
        string storageName)
    {
        foreach (var vectorProperty in definition.Properties.OfType<VectorStoreVectorProperty>())
        {
            vectorProperty.StorageName = storageName;
        }

        return definition;
    }
}
