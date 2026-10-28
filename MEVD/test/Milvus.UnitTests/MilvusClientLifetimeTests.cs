// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusClientLifetimeTests
{
    [Fact]
    public async Task CollectionsPreserveCallerOwnedGrpcChannel()
    {
        using var handler = new MilvusHandler();
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler, DisposeHttpClient = true });
        using var client = new MilvusClient(channel);
        var store = new MilvusVectorStore(client, ownsClient: true);
        using var collection = store.GetCollection<Guid, MilvusTestRecord>("records");
        store.Dispose();
        store.Dispose();
        Assert.True(await collection.CollectionExistsAsync());
        Assert.False(handler.Disposed);
        collection.Dispose();
        Assert.False(handler.Disposed);
        channel.Dispose();
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task BorrowedClientSurvivesDisposalOfStoreAndCollection()
    {
        using var handler = new MilvusHandler();
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler, DisposeHttpClient = true });
        using var client = new MilvusClient(channel);
        using var store = new MilvusVectorStore(client, ownsClient: false);
        using var collection = store.GetCollection<Guid, MilvusTestRecord>("records");
        collection.Dispose();
        store.Dispose();
        Assert.True(await client.HasCollectionAsync("records"));
        Assert.False(handler.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposingCollectionRepeatedlyDoesNotReleaseSiblingShare(bool concurrently)
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var store = new MilvusVectorStore(client, ownsClient: true);
        using var first = store.GetCollection<Guid, MilvusTestRecord>("first");
        using var second = store.GetCollection<Guid, MilvusTestRecord>("second");
        store.Dispose();

        if (concurrently)
        {
            Parallel.For(0, 32, _ => first.Dispose());
        }
        else
        {
            first.Dispose();
            first.Dispose();
        }

        Assert.IsNotType<ObjectDisposedException>(await CallAsync(client));
        second.Dispose();
        Assert.IsType<ObjectDisposedException>(await CallAsync(client));
    }

    [Fact]
    public async Task DisposingStoreRepeatedlyDoesNotReleaseCollectionShares()
    {
        using var client = new MilvusClient("localhost", port: 1);
        using var store = new MilvusVectorStore(client, ownsClient: true);
        using var first = store.GetCollection<Guid, MilvusTestRecord>("first");
        using var second = store.GetCollection<Guid, MilvusTestRecord>("second");

        store.Dispose();
        store.Dispose();
        first.Dispose();

        Assert.IsNotType<ObjectDisposedException>(await CallAsync(client));
        second.Dispose();
        Assert.IsType<ObjectDisposedException>(await CallAsync(client));
    }

    [Fact]
    public async Task WrapsGrpcFailuresWithCollectionMetadata()
    {
        using var handler = new MilvusHandler { GrpcStatus = "14" };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler, DisposeHttpClient = true });
        using var client = new MilvusClient(channel);
        using var collection = new MilvusCollection<Guid, MilvusTestRecord>(client, "records", ownsClient: false);
        var exception = await Assert.ThrowsAsync<VectorStoreException>(() => collection.CollectionExistsAsync());
        Assert.IsType<RpcException>(exception.InnerException);
        Assert.Equal("milvus", exception.VectorStoreSystemName);
        Assert.Equal("records", exception.CollectionName);
        Assert.Equal("CollectionExists", exception.OperationName);
    }

    [Fact]
    public async Task WrapsMilvusFailuresWithOperationMetadata()
    {
        var metadata = new VectorStoreCollectionMetadata { CollectionName = "records", VectorStoreSystemName = "milvus" };
        var error = new MilvusException("server error");
        var exception = await Assert.ThrowsAsync<VectorStoreException>(() => MilvusErrorHandler.RunAsync(metadata, "Upsert", () => Task.FromException(error)));
        Assert.Same(error, exception.InnerException);
        Assert.Equal("Upsert", exception.OperationName);
    }

    [Fact]
    public async Task OwnedClientIsDisposedWhenTheCollectionCannotBeCreated()
    {
        var unsupportedKey = new MilvusClient("localhost", port: 1);
        var invalidModel = new MilvusClient("localhost", port: 1);
        var noDefinition = new MilvusClient("localhost", port: 1);

        Assert.Throws<NotSupportedException>(() => new MilvusCollection<int, MilvusTestRecord>(unsupportedKey, "records", ownsClient: true));
        Assert.Throws<InvalidOperationException>(() => new MilvusCollection<string, InvalidRecord>(invalidModel, "records", ownsClient: true));
        Assert.Throws<ArgumentException>(() => new MilvusDynamicCollection(noDefinition, "records", ownsClient: true, new()));

        Assert.IsType<ObjectDisposedException>(await CallAsync(unsupportedKey));
        Assert.IsType<ObjectDisposedException>(await CallAsync(invalidModel));
        Assert.IsType<ObjectDisposedException>(await CallAsync(noDefinition));
    }

    [Fact]
    public async Task BorrowedClientSurvivesACollectionThatCannotBeCreated()
    {
        using var client = new MilvusClient("localhost", port: 1);

        Assert.Throws<NotSupportedException>(() => new MilvusCollection<int, MilvusTestRecord>(client, "records", ownsClient: false));

        Assert.IsNotType<ObjectDisposedException>(await CallAsync(client));
    }

    [Fact]
    public async Task StoreKeepsItsClientWhenOneOfItsCollectionsCannotBeCreated()
    {
        var client = new MilvusClient("localhost", port: 1);
        var store = new MilvusVectorStore(client, ownsClient: true);

        Assert.Throws<NotSupportedException>(() => store.GetCollection<int, MilvusTestRecord>("records"));
        Assert.IsNotType<ObjectDisposedException>(await CallAsync(client));

        store.Dispose();
        Assert.IsType<ObjectDisposedException>(await CallAsync(client));
    }

    // Calls the client with a canceled token: a disposed client throws ObjectDisposedException, another the cancellation,
    // without contacting a server.
    private static async Task<Exception?> CallAsync(MilvusClient client)
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        return await Record.ExceptionAsync(() => client.HasCollectionAsync("records", cancellationToken: canceled.Token));
    }

    public sealed class InvalidRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public int Count { get; set; }

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    private sealed class MilvusHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        public string GrpcStatus { get; init; } = "0";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // gRPC frame containing BoolResponse { status: {}, value: true }.
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = new Version(2, 0),
                Content = new ByteArrayContent(new byte[] { 0, 0, 0, 0, 4, 10, 0, 16, 1 }),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", GrpcStatus);
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
