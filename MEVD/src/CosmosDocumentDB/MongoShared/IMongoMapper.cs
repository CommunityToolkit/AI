// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Extensions.AI;
using MongoDB.Bson;

namespace CommunityToolkit.VectorData.CosmosDocumentDB;

internal interface IMongoMapper<TRecord>
{
    /// <summary>
    /// Maps from the consumer record data model to the storage model.
    /// </summary>
    BsonDocument MapFromDataToStorageModel(TRecord dataModel, int recordIndex, IReadOnlyList<Embedding>?[]? generatedEmbeddings);

    /// <summary>
    /// Maps from the storage model to the consumer record data model.
    /// </summary>
    TRecord MapFromStorageToDataModel(BsonDocument storageModel, bool includeVectors);
}
