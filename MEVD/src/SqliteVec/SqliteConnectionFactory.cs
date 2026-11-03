// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Data.Sqlite;

namespace CommunityToolkit.VectorData.SqliteVec;

/// <summary>
/// Creates the connections used by the vector store and its collections.
/// </summary>
internal static class SqliteConnectionFactory
{
    /// <summary>
    /// Opens a connection, runs the shared setup steps in order, and hands it to the caller.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned connection. If any setup step fails, the connection is disposed here
    /// because the caller never receives it.
    /// </remarks>
    public static async ValueTask<SqliteConnection> OpenAsync(
        string connectionString,
        Func<SqliteConnection, CancellationToken, ValueTask>? configureConnection,
        bool loadVector,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (loadVector)
            {
                connection.LoadVector();
            }

            if (configureConnection != null)
            {
                await configureConnection.Invoke(connection, cancellationToken).ConfigureAwait(false);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
