// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ChromaDB.Client;
using CommunityToolkit.VectorData.Chroma;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;

namespace Chroma.ConformanceTests.Support;

#pragma warning disable CA1001 // Type owns disposable fields but is not disposable

internal sealed class ChromaTestStore : TestStore
{
    private const ushort ChromaPort = 8000;

    public static ChromaTestStore Instance { get; } = new();

    // Chroma indexes vectors with HNSW only
    public override string DefaultIndexKind => IndexKind.Hnsw;

    // Only when no external instance is configured, see ChromaTestEnvironment.
    private readonly IContainer? _container = ChromaTestEnvironment.IsConnectionStringDefined
        ? null
        : new ContainerBuilder("chromadb/chroma:1.5.9")
            .WithPortBinding(ChromaPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPath("/api/v2/heartbeat").ForPort(ChromaPort)))
            .Build();

    private ChromaClient? _client;

    /// <summary>
    /// Chroma normalizes the vectors of cosine collections, so the vectors it returns
    /// can differ from the upserted ones in the last digits; we can only check that
    /// a vector was returned.
    /// </summary>
    public override bool VectorsComparable => false;

    public ChromaClient Client => this._client ?? throw new InvalidOperationException("Not initialized");

    /// <summary>
    /// Whether the tests run against Chroma Cloud, the only Chroma with the Search API and the sparse vector indexes that hybrid search needs.
    /// </summary>
    public static bool IsChromaCloud => ChromaTestEnvironment.IsConnectionStringDefined
        && ChromaConfigurationOptions.FromConnectionString(ChromaTestEnvironment.ConnectionString!).Uri.Host.EndsWith(".trychroma.com", StringComparison.OrdinalIgnoreCase);

    public ChromaVectorStore GetVectorStore(ChromaVectorStoreOptions options)
        => new(this.Client, ownsClient: false, options); // The client is shared, it's not owned by the vector store.

    private ChromaTestStore()
    {
    }

    protected override async Task StartAsync()
    {
        ChromaConfigurationOptions options;
        if (this._container is not null)
        {
            await this._container.StartAsync();
            options = new ChromaConfigurationOptions(
                new UriBuilder(Uri.UriSchemeHttp, this._container.Hostname, this._container.GetMappedPublicPort(ChromaPort)).ToString());
        }
        else
        {
            options = ChromaConfigurationOptions.FromConnectionString(ChromaTestEnvironment.ConnectionString!);
        }

        this._client = new ChromaClient(options);

        // It's a shared static instance, we don't want any of the tests to dispose the client.
        this.DefaultVectorStore = new ChromaVectorStore(this._client, ownsClient: false);
    }

    protected override async Task StopAsync()
    {
        this._client?.Dispose();

        if (this._container is not null)
        {
            await this._container.StopAsync();
        }
    }
}
