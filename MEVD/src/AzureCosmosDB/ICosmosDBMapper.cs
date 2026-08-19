// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Nodes;
using MEAI = Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.AzureCosmosDB;

internal interface ICosmosDBMapper<TRecord>
{
    /// <summary>
    /// Maps from the consumer record data model to the storage model.
    /// </summary>
    JsonObject MapFromDataToStorageModel(TRecord dataModel, int recordIndex, IReadOnlyList<MEAI.Embedding>?[]? generatedEmbeddings);

    /// <summary>
    /// Maps from the storage model to the consumer record data model.
    /// </summary>
    TRecord MapFromStorageToDataModel(JsonObject storageModel, bool includeVectors);
}
