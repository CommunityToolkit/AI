// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ChromaDB.Client;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// The <see cref="ChromaClient"/> of a vector store or of a collection. A vector store shares it with the collections it returns,
/// and the <see cref="ChromaClient"/> it owns is disposed when the last of them is disposed.
/// </summary>
internal sealed class SharedChromaClient : IDisposable
{
    private readonly ChromaClient? _ownedClient;
    private int _referenceCount = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedChromaClient"/> class.
    /// </summary>
    /// <param name="chromaClient">Chroma client that can be used to manage the collections and records in a Chroma store.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="chromaClient"/> is disposed with the last of the vector store and its collections.</param>
    public SharedChromaClient(ChromaClient chromaClient, bool ownsClient)
    {
        Throw.IfNull(chromaClient);

        Client = chromaClient;
        _ownedClient = ownsClient ? chromaClient : null;
    }

    /// <summary>
    /// Gets the client.
    /// </summary>
    public ChromaClient Client { get; }

    /// <summary>
    /// Gets the database the client works in, or <see langword="null"/> for the default database of the server.
    /// </summary>
    public string? DatabaseName => Client.Options.Database;

    /// <summary>
    /// Gets this client for one more user, a collection of the vector store, which disposes it too.
    /// </summary>
    public SharedChromaClient Share()
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
