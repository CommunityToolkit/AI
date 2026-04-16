// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.Redis;

internal interface IRedisJsonMapper<TRecord>
{
    /// <summary>
    /// Maps from the consumer record data model to the storage model.
    /// </summary>
    (string Key, JsonNode Node) MapFromDataToStorageModel(TRecord dataModel, int recordIndex, IReadOnlyList<Embedding>?[]? generatedEmbeddings);

    /// <summary>
    /// Maps from the storage model to the consumer record data model.
    /// </summary>
    TRecord MapFromStorageToDataModel((string Key, JsonNode Node) storageModel, bool includeVectors);
}
