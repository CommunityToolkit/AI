// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;
using Milvus.Client.V2;
using Milvus.Client.V2.Types;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

/// <summary>Creates <see cref="MilvusClientV2"/> instances for tests that do not reach a real server.</summary>
internal static class TestClients
{
    public static MilvusClientV2 Create(int port = 19530, string? database = null)
        => new(new ConnectConfig { Uri = $"http://localhost:{port}", Database = database });

    /// <summary>
    /// Creates a client whose gRPC calls go to <paramref name="handler"/>. Milvus.Client.V2 cannot take a caller-owned
    /// channel, so the channel, and with it the handler, is owned and disposed by the client.
    /// </summary>
    public static MilvusClientV2 FromHandler(HttpMessageHandler handler, string? database = null)
        => new(new ConnectConfig
        {
            Uri = "http://localhost:19530",
            Database = database,
            ChannelOptions = new() { HttpHandler = handler, DisposeHttpClient = true },
        });
}
