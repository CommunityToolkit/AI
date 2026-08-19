// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.AzureCosmosDB;

internal static class CosmosConstants
{
    internal const string VectorStoreSystemName = "azure.cosmosdbnosql";

    /// <summary>
    /// Reserved key property name in Azure Cosmos NoSQL.
    /// </summary>
    internal const string ReservedKeyPropertyName = "id";

    /// <summary>
    /// Variable name for table in Azure Cosmos NoSQL queries.
    /// Can be any string. Example: "SELECT x.Name FROM x".
    /// </summary>
    internal const char ContainerAlias = 'x';
}
