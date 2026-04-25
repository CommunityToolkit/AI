// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.PgVector;

/// <summary>
/// Options when creating a <see cref="PostgresCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class PostgresCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly PostgresCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresCollectionOptions"/> class.
    /// </summary>
    public PostgresCollectionOptions()
    {
    }

    internal PostgresCollectionOptions(PostgresCollectionOptions? source) : base(source)
    {
        Schema = source?.Schema;
    }

    /// <summary>
    /// Gets or sets the database schema.
    /// </summary>
    public string? Schema { get; set; }
}
