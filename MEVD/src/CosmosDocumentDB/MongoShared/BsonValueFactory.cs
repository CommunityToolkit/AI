// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;

namespace CommunityToolkit.VectorData.CosmosDocumentDB;

/// <summary>
/// A class that constructs the correct BsonValue for a given CLR type.
/// </summary>
internal static class BsonValueFactory
{
    /// <summary>
    /// Create a BsonValue for the given CLR type.
    /// </summary>
    /// <param name="value">The CLR object to create a BSON value for.</param>
    /// <returns>The appropriate <see cref="BsonValue"/> for that CLR type.</returns>
    public static BsonValue Create(object? value)
        => value switch
        {
            null => BsonNull.Value,
            Guid v => new BsonBinaryData(v, GuidRepresentation.Standard),
            DateTimeOffset v => new BsonDateTime(v.UtcDateTime),
#if NET
            DateOnly v => new BsonDateTime(v.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
#endif
            object[] v => new BsonArray(Array.ConvertAll(v, Create)),
            Array v => new BsonArray(v),
            IEnumerable<object> v => new BsonArray(v.Select(Create)),

            _ => BsonValue.Create(value)
        };
}
