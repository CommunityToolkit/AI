// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Data.Sqlite;
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
        ConnectionInitializer = source?.ConnectionInitializer;
    }

    /// <summary>
    /// Custom virtual table name to store vectors.
    /// </summary>
    /// <remarks>
    /// If not provided, collection name with prefix will be used as virtual table name.
    /// </remarks>
    public string? VectorVirtualTableName { get; set; }

    /// <summary>
    /// Gets or sets a callback that runs on every connection this collection opens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If defined, the callback runs after the connection is opened and the sqlite-vec extension is loaded.
    /// </para>
    /// <para>
    /// Use it for per-connection settings that have no connection string equivalent, such as
    /// <c>PRAGMA synchronous=NORMAL</c>, <c>PRAGMA cache_size</c> or <c>PRAGMA temp_store=MEMORY</c>, or for loading additional extensions.
    /// </para>
    /// <para>
    /// When the collection is created from a <see cref="SqliteVectorStore"/>, the callback configured on the vector store options
    /// is used; this property applies to collections constructed directly.
    /// </para>
    /// <para>
    /// The collection owns the connection and disposes it when the operation completes. The callback must not close or dispose
    /// the connection, and must not leave a transaction or a data reader open. If the callback throws, the operation fails and
    /// the exception is surfaced to the caller. The callback can run on several connections at the same time, so it must be
    /// thread-safe.
    /// </para>
    /// </remarks>
    public Action<SqliteConnection>? ConnectionInitializer { get; set; }
}
