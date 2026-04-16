// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.Redis;

/// <summary>
/// Options when creating a <see cref="RedisVectorStore"/>.
/// </summary>
public sealed class RedisVectorStoreOptions
{
    internal static readonly RedisVectorStoreOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisVectorStoreOptions"/> class.
    /// </summary>
    public RedisVectorStoreOptions()
    {
    }

    internal RedisVectorStoreOptions(RedisVectorStoreOptions? source)
    {
        StorageType = source?.StorageType ?? Default.StorageType;
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Indicates the way in which data should be stored in redis. Default is <see cref="RedisStorageType.Json"/>.
    /// </summary>
    public RedisStorageType? StorageType { get; set; } = RedisStorageType.Json;

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
