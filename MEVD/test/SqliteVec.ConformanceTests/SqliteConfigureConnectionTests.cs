// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data;
using CommunityToolkit.VectorData.SqliteVec;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace SqliteVec.ConformanceTests;

public sealed class SqliteConfigureConnectionTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"sqlitevec-init-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_databasePath};Pooling=false";

    [Fact]
    public async Task CollectionCallbackRunsOncePerOperationAndCanSetPragmas()
    {
        var invocations = 0;
        var synchronousValues = new List<long>();

        var options = new SqliteCollectionOptions
        {
            ConfigureConnection = async (connection, ct) =>
            {
                invocations++;

                using var setCommand = connection.CreateCommand();
                setCommand.CommandText = "PRAGMA synchronous=NORMAL";
                await setCommand.ExecuteNonQueryAsync(ct);

                using var getCommand = connection.CreateCommand();
                getCommand.CommandText = "PRAGMA synchronous";
                using var reader = await getCommand.ExecuteReaderAsync(ct);
                Assert.True(await reader.ReadAsync(ct));
                synchronousValues.Add(reader.GetInt64(0));
            }
        };

        var collection = new SqliteCollection<string, TestRecord>(ConnectionString, "CallbackCollection", options);

        await collection.EnsureCollectionExistsAsync();
        await collection.UpsertAsync(new TestRecord
        {
            Key = "key1",
            Text = "hello",
            Vector = new([1f, 2f, 3f, 4f])
        });
        Assert.NotNull(await collection.GetAsync("key1"));
        Assert.True(await collection.CollectionExistsAsync());

        // Each operation opens its own connection and runs the callback on it.
        Assert.Equal(4, invocations);
        // NORMAL is 1, FULL is 2 (default).
        Assert.All(synchronousValues, value => Assert.Equal(1, value));
    }

    [Fact]
    public async Task StoreCallbackRunsForStoreOperationsAndFlowsToCollections()
    {
        var invocations = 0;

        var store = new SqliteVectorStore(ConnectionString, new()
        {
            ConfigureConnection = async (connection, ct) => invocations++
        });

        await foreach (var _ in store.ListCollectionNamesAsync())
        {
        }

        var collection = store.GetCollection<string, TestRecord>("CallbackStoreCollection");
        await collection.EnsureCollectionExistsAsync();
        await collection.UpsertAsync(new TestRecord
        {
            Key = "key2",
            Text = "hello",
            Vector = new([1f, 2f, 3f, 4f])
        });

        // One connection for ListCollectionNamesAsync, one each for ensure and upsert on the collection.
        Assert.Equal(3, invocations);
    }

    [Fact]
    public async Task StoreCallbackRunsForDynamicCollectionOperations()
    {
        var invocations = 0;

        var store = new SqliteVectorStore(ConnectionString, new()
        {
            ConfigureConnection = async (connection, ct) => invocations++
        });

        Assert.False(await store.CollectionExistsAsync("MissingCollection"));
        await store.EnsureCollectionDeletedAsync("MissingCollection");

        // CollectionExistsAsync and EnsureCollectionDeletedAsync each build a dynamic collection which must call the callback
        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task CallbackRunsAfterVectorExtensionIsLoaded()
    {
        string? vecVersion = null;

        var options = new SqliteCollectionOptions
        {
            // vec_version is provided by the sqlite-vec extension, so this only works if the
            // extension is loaded before the callback runs.
            ConfigureConnection = async (connection, ct) =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT vec_version()";
                vecVersion = await command.ExecuteScalarAsync(ct) as string;
            }
        };

        var collection = new SqliteCollection<string, TestRecord>(ConnectionString, "CallbackExtensionCollection", options);
        await collection.EnsureCollectionExistsAsync();

        Assert.False(string.IsNullOrEmpty(vecVersion));
    }

    [Fact]
    public async Task StoreCallbackRunsAfterVectorExtensionIsLoaded()
    {
        string? vecVersion = null;

        var store = new SqliteVectorStore(ConnectionString, new()
        {
            ConfigureConnection = async (connection, ct) =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT vec_version()";
                vecVersion = await command.ExecuteScalarAsync(ct) as string;
            }
        });

        await foreach (var _ in store.ListCollectionNamesAsync())
        {
        }

        Assert.False(string.IsNullOrEmpty(vecVersion));
    }

    [Fact]
    public async Task ThrowingCallbackPropagatesExceptionAndDisposesConnection()
    {
        SqliteConnection? capturedConnection = null;

        var options = new SqliteCollectionOptions
        {
            ConfigureConnection = async (connection, ct) =>
            {
                capturedConnection = connection;
                throw new InvalidOperationException("Callback failed.");
            }
        };

        var collection = new SqliteCollection<string, TestRecord>(ConnectionString, "CallbackThrowCollection", options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => collection.EnsureCollectionExistsAsync());
        Assert.Equal("Callback failed.", exception.Message);

        // The caller never received the connection, so the connector must dispose it itself.
        Assert.NotNull(capturedConnection);
        Assert.Equal(ConnectionState.Closed, capturedConnection!.State);
    }

    [Fact]
    public async Task CallbackReceivesOperationCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken captured = default;

        var options = new SqliteCollectionOptions
        {
            ConfigureConnection = async (connection, ct) => captured = ct
        };

        var collection = new SqliteCollection<string, TestRecord>(ConnectionString, "CallbackTokenCollection", options);
        await collection.EnsureCollectionExistsAsync(cts.Token);

        Assert.Equal(cts.Token, captured);
    }

    public void Dispose()
    {
        File.Delete(_databasePath);
    }

    private sealed class TestRecord
    {
        [VectorStoreKey]
        public string? Key { get; set; }

        [VectorStoreData]
        public string? Text { get; set; }

        [VectorStoreVector(dimensions: 4, DistanceFunction = DistanceFunction.EuclideanDistance)]
        public ReadOnlyMemory<float>? Vector { get; set; }
    }
}
