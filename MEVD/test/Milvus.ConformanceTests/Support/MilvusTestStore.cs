// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.VectorData.Milvus;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Testcontainers.Milvus;
using VectorData.ConformanceTests.Support;

namespace Milvus.ConformanceTests.Support;

#pragma warning disable CA1001 // The base TestStore controls resource lifetime.
internal sealed class MilvusTestStore : TestStore
{
    public static MilvusTestStore Instance { get; } = new();
    public override string DefaultIndexKind => IndexKind.Flat;
    private readonly MilvusContainer? _container = Environment.GetEnvironmentVariable("MILVUS_HOST") is null
        ? new MilvusBuilder("milvusdb/milvus:v2.6.25").Build() : null;
    public string Host { get; private set; } = null!;
    public int Port { get; private set; }
    private MilvusClient? _client;
    public MilvusClient Client => _client ?? throw new InvalidOperationException("Not initialized");
    public MilvusVectorStore GetVectorStore(MilvusVectorStoreOptions options) => new(Client, ownsClient: false, options);

    private MilvusTestStore()
    {
    }

    protected override async Task StartAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
        }
        var host = Environment.GetEnvironmentVariable("MILVUS_HOST") ?? _container!.Hostname;
        var port = int.Parse(Environment.GetEnvironmentVariable("MILVUS_PORT") ?? "19530", System.Globalization.CultureInfo.InvariantCulture);
        Host = host;
        Port = _container?.GetMappedPublicPort(19530) ?? port;
        _client = new MilvusClient(Host, Port);
        DefaultVectorStore = new MilvusVectorStore(_client, ownsClient: false);
    }

    protected override async Task StopAsync()
    {
        DefaultVectorStore.Dispose();
        _client?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
