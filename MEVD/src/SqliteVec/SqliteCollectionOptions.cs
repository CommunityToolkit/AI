// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.SqliteVec;

/// <summary>
/// Options when creating a <see cref="SqliteCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class SqliteCollectionOptions : VectorStoreCollectionOptions
{
    internal static readonly SqliteCollectionOptions Default = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteCollectionOptions"/> class.
    /// </summary>
    public SqliteCollectionOptions()
    {
    }

    internal SqliteCollectionOptions(SqliteCollectionOptions? source) : base(source)
    {
        VectorVirtualTableName = source?.VectorVirtualTableName;
    }

    /// <summary>
    /// Custom virtual table name to store vectors.
    /// </summary>
    /// <remarks>
    /// If not provided, collection name with prefix will be used as virtual table name.
    /// </remarks>
    public string? VectorVirtualTableName { get; set; }
}
