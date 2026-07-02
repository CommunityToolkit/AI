// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.SqlServer;

/// <summary>
/// Options when creating a <see cref="SqlServerCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class SqlServerCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly SqlServerCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerCollectionOptions"/> class.
    /// </summary>
    public SqlServerCollectionOptions()
    {
    }

    internal SqlServerCollectionOptions(SqlServerCollectionOptions? source) : base(source)
    {
        this.Schema = source?.Schema;
    }

    /// <summary>
    /// Gets or sets the database schema.
    /// </summary>
    public string? Schema { get; set; }
}
