// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.Client;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Milvus;

/// <summary>
/// The <see cref="MilvusClient"/> of a vector store or of a collection. A vector store shares it with the collections it returns,
/// and the <see cref="MilvusClient"/> it owns is disposed when the last of them is disposed.
/// </summary>
internal sealed class SharedMilvusClient : IDisposable
{
    private readonly MilvusClient? _ownedClient;
    private int _referenceCount = 1;

    /// <summary>
    /// Gets the client.
    /// </summary>
    public MilvusClient Client { get; }

    /// <summary>
    /// Gets the database name for telemetry, or <see langword="null"/> for a client created by the caller: the SDK does not
    /// expose the configured database.
    /// </summary>
    public string? DatabaseName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedMilvusClient"/> class.
    /// </summary>
    /// <param name="milvusClient">Milvus client that can be used to manage the collections and records in a Milvus store.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="milvusClient"/> is disposed with the last of the vector store and its collections.</param>
    /// <param name="databaseName">
    /// The database the SDK client connects to, for telemetry: known to the host-based registrations, which create the client.
    /// </param>
    public SharedMilvusClient(MilvusClient milvusClient, bool ownsClient, string? databaseName = null)
    {
        Throw.IfNull(milvusClient);

        Client = milvusClient;
        DatabaseName = databaseName;
        _ownedClient = ownsClient ? milvusClient : null;
    }

    /// <summary>
    /// Gets this client for one more user, a collection of the vector store, which disposes it too.
    /// </summary>
    public SharedMilvusClient Share()
    {
        if (_ownedClient is not null)
        {
            Interlocked.Increment(ref _referenceCount);
        }

        return this;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownedClient is not null && Interlocked.Decrement(ref _referenceCount) == 0)
        {
            _ownedClient.Dispose();
        }
    }
}
