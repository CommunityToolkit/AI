// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusSearchResponseTests
{
    [Fact]
    public async Task SearchThrowsWhenTheResponseHasFewerRowsThanHits()
    {
        // Two hits, but the output fields hold a single row.
        using var handler = new SearchHandler(ids: ["a", "b"], rowIds: ["a"]);
        using var client = TestClients.FromHandler(handler);
        using var collection = new MilvusCollection<string, KeyOnlyRecord>(client, "records", ownsClient: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await collection.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), 2).ToListAsync());
    }

    [Fact]
    public async Task SearchReturnsOneRecordPerHit()
    {
        using var handler = new SearchHandler(ids: ["a", "b"], rowIds: ["a", "b"]);
        using var client = TestClients.FromHandler(handler);
        using var collection = new MilvusCollection<string, KeyOnlyRecord>(client, "records", ownsClient: false);

        var results = await collection.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), 2).ToListAsync();

        Assert.Equal(["a", "b"], results.Select(r => r.Record.Id));
    }

    public sealed class KeyOnlyRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    // Answers Search with a SearchResults message built by hand, and every other gRPC call with an empty status.
    private sealed class SearchHandler(string[] ids, string[] rowIds) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var message = method == "Search" ? SearchResults() : [10, 0, 16, 1];
            var frame = new byte[5 + message.Length];
            frame[1] = (byte)(message.Length >> 24);
            frame[2] = (byte)(message.Length >> 16);
            frame[3] = (byte)(message.Length >> 8);
            frame[4] = (byte)message.Length;
            message.CopyTo(frame, 5);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = new Version(2, 0), Content = new ByteArrayContent(frame) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return Task.FromResult(response);
        }

        // milvus.proto.milvus.SearchResults { status = 1; results = 2 } with
        // milvus.proto.schema.SearchResultData { num_queries = 1; top_k = 2; fields_data = 3; scores = 4; ids = 5; topks = 6 }.
        private byte[] SearchResults()
        {
            var keyColumn = Concat(
                Field(1, Varint(21)), // type: VarChar
                Field(2, Bytes(Encoding.UTF8.GetBytes("Id"))), // field_name
                Field(3, Bytes(Field(6, Bytes(Strings(rowIds)))))); // scalars.string_data
            var scores = Concat(ids.Select((_, i) => BitConverter.GetBytes(1f - (0.1f * i))).ToArray());
            var data = Concat(
                Field(1, Varint(1)),
                Field(2, Varint((ulong)ids.Length)),
                Field(3, Bytes(keyColumn)),
                Field(4, Bytes(scores)),
                Field(5, Bytes(Field(2, Bytes(Strings(ids))))), // ids.str_id
                Field(6, Bytes(Varint((ulong)ids.Length).Item2)));
            return Concat(Field(1, Bytes([])), Field(2, Bytes(data)));
        }

        private static byte[] Strings(IEnumerable<string> values)
            => Concat(values.Select(v => Field(1, Bytes(Encoding.UTF8.GetBytes(v)))).ToArray());

        private static byte[] Field(int number, (int WireType, byte[] Payload) value)
            => Concat(Varint((ulong)((number << 3) | value.WireType)).Item2, value.Payload);

        private static (int, byte[]) Varint(ulong value)
        {
            var bytes = new List<byte>();
            do
            {
                var b = (byte)(value & 0x7F);
                value >>= 7;
                bytes.Add(value == 0 ? b : (byte)(b | 0x80));
            }
            while (value != 0);
            return (0, bytes.ToArray());
        }

        private static (int, byte[]) Bytes(byte[] value) => (2, Concat(Varint((ulong)value.Length).Item2, value));

        private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    }
}
