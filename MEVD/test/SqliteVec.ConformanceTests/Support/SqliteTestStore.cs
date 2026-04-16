// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.SqliteVec;
using Microsoft.Data.Sqlite;
using VectorData.ConformanceTests.Support;

namespace SqliteVec.ConformanceTests.Support;

internal sealed class SqliteTestStore : TestStore
{
    private string? _databasePath;

    private string? _connectionString;
    public string ConnectionString => this._connectionString ?? throw new InvalidOperationException("Not initialized");

    public static SqliteTestStore Instance { get; } = new();

    public override string DefaultDistanceFunction => Microsoft.Extensions.VectorData.DistanceFunction.CosineDistance;

    public SqliteVectorStore GetVectorStore(SqliteVectorStoreOptions options)
        => new(this.ConnectionString, options);

    private SqliteTestStore()
    {
    }

    protected override Task StartAsync()
    {
        // Verify that the sqlite_vec extension can be loaded; fail early with a clear message if not.
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            connection.LoadVector();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The sqlite_vec native extension could not be loaded. " +
                "Make sure the appropriate native dependencies are available.",
                ex);
        }

        this._databasePath = Path.GetTempFileName();
        this._connectionString = $"Data Source={this._databasePath};Pooling=false";
        this.DefaultVectorStore = new SqliteVectorStore(this._connectionString);
        return Task.CompletedTask;
    }

    protected override Task StopAsync()
    {
        File.Delete(this._databasePath!);
        this._databasePath = null;
        return Task.CompletedTask;
    }
}
