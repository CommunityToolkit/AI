// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.AzureCosmosDB;

internal static class CosmosDBConstants
{
    internal const string VectorStoreSystemName = "azure.cosmosdbnosql";

    /// <summary>
    /// Reserved key property name in Azure CosmosDB NoSQL.
    /// </summary>
    internal const string ReservedKeyPropertyName = "id";

    /// <summary>
    /// Variable name for table in Azure CosmosDB NoSQL queries.
    /// Can be any string. Example: "SELECT x.Name FROM x".
    /// </summary>
    internal const char ContainerAlias = 'x';
}
