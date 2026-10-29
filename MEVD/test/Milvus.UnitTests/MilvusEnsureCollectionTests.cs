// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusEnsureCollectionTests
{
    [Fact]
    public async Task CreatesTheIndexesWithTheCollection()
    {
        using var handler = new RecordingHandler { CollectionExists = false };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler, DisposeHttpClient = true });
        using var client = new MilvusClient(channel);
        using var collection = new MilvusCollection<string, MilvusHybridSearchTests.TextRecord>(client, "records", ownsClient: false);

        await collection.EnsureCollectionExistsAsync();

        Assert.Contains("CreateCollection", handler.Methods);
        // The vector index and the BM25 index of the full-text property.
        Assert.Equal(2, handler.Methods.Count(m => m == "CreateIndex"));
        Assert.Contains("LoadCollection", handler.Methods);
    }

    [Fact]
    public async Task LeavesTheIndexesOfAnExistingCollectionAsTheyAre()
    {
        using var handler = new RecordingHandler { CollectionExists = true };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler, DisposeHttpClient = true });
        using var client = new MilvusClient(channel);
        using var collection = new MilvusCollection<string, MilvusHybridSearchTests.TextRecord>(client, "records", ownsClient: false);

        await collection.EnsureCollectionExistsAsync();

        Assert.DoesNotContain("CreateCollection", handler.Methods);
        Assert.DoesNotContain("CreateIndex", handler.Methods);
        Assert.Contains("LoadCollection", handler.Methods);
    }

    [Fact(Timeout = 30000)]
    public async Task ReportsALoadThatDoesNotEndAsAVectorStoreException()
    {
        // The server reports the load at 1% at each poll.
        using var handler = new RecordingHandler { CollectionExists = true, LoadingProgress = 1 };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler, DisposeHttpClient = true });
        using var client = new MilvusClient(channel);
        using var collection = new MilvusCollection<string, MilvusHybridSearchTests.TextRecord>(client, "records", ownsClient: false,
            options: new() { LoadTimeout = TimeSpan.FromMilliseconds(100) });

        var exception = await Assert.ThrowsAsync<VectorStoreException>(() => collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken));

        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Equal("EnsureCollectionExists", exception.OperationName);
        Assert.Equal("records", exception.CollectionName);
    }

    [Fact]
    public void WaitsForTheLoadLongerThanMilvusDoes()
    {
        // Milvus 2.6.25 sets queryCoord.loadTimeoutSeconds to 600 in its default configuration.
        Assert.Equal(TimeSpan.FromMinutes(11), new MilvusCollectionOptions().LoadTimeout);
    }

    [Fact]
    public void CopiesLoadTimeout()
    {
        var options = new MilvusCollectionOptions { LoadTimeout = TimeSpan.FromSeconds(30) };

        Assert.Equal(options.LoadTimeout, new MilvusCollectionOptions(options).LoadTimeout);
        Assert.Equal(TimeSpan.FromMinutes(11), new MilvusCollectionOptions(null).LoadTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveLoadTimeout(int seconds)
    {
        using var client = new MilvusClient("localhost", port: 1);
        var options = new MilvusCollectionOptions { LoadTimeout = TimeSpan.FromSeconds(seconds) };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MilvusCollection<string, MilvusHybridSearchTests.TextRecord>(client, "records", ownsClient: false, options: options));
    }

    [Fact]
    public void DefaultsAndCopiesHnswSettings()
    {
        var defaults = new MilvusCollectionOptions(null);
        Assert.Equal(16, defaults.HnswM);
        Assert.Equal(200, defaults.HnswEfConstruction);

        var copy = new MilvusCollectionOptions(new() { HnswM = 32, HnswEfConstruction = 300 });
        Assert.Equal(32, copy.HnswM);
        Assert.Equal(300, copy.HnswEfConstruction);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(-1, 200)]
    [InlineData(16, 0)]
    [InlineData(16, -1)]
    public void RejectsNonPositiveHnswSettings(int m, int efConstruction)
    {
        using var client = new MilvusClient("localhost", port: 1);
        var options = new MilvusCollectionOptions { HnswM = m, HnswEfConstruction = efConstruction };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MilvusCollection<string, MilvusHybridSearchTests.TextRecord>(client, "records", ownsClient: false, options: options));
    }

    [Fact]
    public async Task SendsConfiguredHnswSettingsWhenCreatingIndex()
    {
        using var handler = new RecordingHandler();
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler, DisposeHttpClient = true });
        using var client = new MilvusClient(channel);
        using var collection = new MilvusCollection<string, HnswRecord>(client, "records", ownsClient: false,
            options: new() { HnswM = 32, HnswEfConstruction = 300 });

        await collection.EnsureCollectionExistsAsync();

        var body = Assert.Single(handler.IndexRequests);
        Assert.Contains("M", body);
        Assert.Contains("32", body);
        Assert.Contains("efConstruction", body);
        Assert.Contains("300", body);
    }

    public sealed class HnswRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(3, IndexKind = IndexKind.Hnsw)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    // Answers each gRPC call of Milvus.Client with an empty status, and records the methods called.
    internal sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<string> _methods = [];

        public List<string> IndexRequests { get; } = [];

        public bool CollectionExists { get; init; }

        public int LoadingProgress { get; init; } = 100;

        public IReadOnlyList<string> Methods
        {
            get
            {
                lock (_methods)
                {
                    return _methods.ToList();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            if (method == "CreateIndex")
            {
                var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                IndexRequests.Add(System.Text.Encoding.UTF8.GetString(body));
            }
            lock (_methods)
            {
                _methods.Add(method);
            }

            // A gRPC frame of a message with an empty status as field 1 and a varint as field 2: the value of a BoolResponse,
            // the progress of a GetLoadingProgressResponse, and a field the other responses ignore.
            var value = method switch
            {
                "HasCollection" => CollectionExists ? 1 : 0,
                "GetLoadingProgress" => LoadingProgress,
                _ => 1,
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = new Version(2, 0),
                Content = new ByteArrayContent([0, 0, 0, 0, 4, 10, 0, 16, (byte)value]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return response;
        }
    }
}
