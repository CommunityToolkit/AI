// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.Redis;

/// <summary>
/// Indicates the way in which data is stored in redis.
/// </summary>
public enum RedisStorageType
{
    /// <summary>
    /// Data is stored as JSON.
    /// </summary>
    Json,

    /// <summary>
    /// Data is stored as collections of field-value pairs.
    /// </summary>
    HashSet
}
