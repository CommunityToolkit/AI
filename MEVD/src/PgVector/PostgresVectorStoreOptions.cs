// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace CommunityToolkit.VectorData.PgVector;

/// <summary>
/// Options when creating a <see cref="PostgresVectorStore"/>.
/// </summary>
public sealed class PostgresVectorStoreOptions
{
    internal static readonly PostgresVectorStoreOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVectorStoreOptions"/> class.
    /// </summary>
    public PostgresVectorStoreOptions()
    {
    }

    internal PostgresVectorStoreOptions(PostgresVectorStoreOptions? source)
    {
        Schema = source?.Schema;
        EmbeddingGenerator = source?.EmbeddingGenerator;
    }

    /// <summary>
    /// Gets or sets the database schema.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets the default embedding generator to use when generating vectors embeddings with this vector store.
    /// </summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
