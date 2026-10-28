# CommunityToolkit.VectorData.Milvus

Milvus provider for [Microsoft.Extensions.VectorData](https://learn.microsoft.com/dotnet/ai/vector-stores/overview), by the .NET Community Toolkit.

[Milvus](https://milvus.io/) is an open-source vector database for AI applications.

The provider is built on [Milvus.Client](https://www.nuget.org/packages/Milvus.Client), the Milvus C# SDK.

Requires Milvus 2.6 or later.

## Quick start

1. Run Milvus with Docker, as a single container with embedded etcd and local storage:

```bash
docker run -d --name milvus -p 19530:19530 -e DEPLOY_MODE=STANDALONE -e ETCD_USE_EMBED=true -e COMMON_STORAGETYPE=local milvusdb/milvus:v2.6.25 milvus run standalone
```

For other setups, see the [standalone installation instructions](https://milvus.io/docs/install_standalone-docker.md).

Host-based dependency injection registrations use HTTPS by default. For the local plaintext container above, explicitly pass `https: false`.

2. Install the NuGet package:

```bash
dotnet add package CommunityToolkit.VectorData.Milvus
```

For more information, see the [Microsoft.Extensions.VectorData documentation](https://learn.microsoft.com/dotnet/ai/vector-stores/overview).

## Limitations

- Milvus 2.5 is not supported: on Milvus 2.5.27, an upsert of a record with a string property fails with `the length of valid_data of field(...) is wrong`.
- Keys can be `long`, `string`, or `Guid`. Auto-generation is supported only for Guid keys and happens in the provider before upsert.
- Whole-array equality filters, date/time data, and ordered retrieval are not supported. Array membership via `Contains` and `Any` is supported.
- `IsFullTextIndexed` is supported on string properties. Milvus analyzes the text with its default analyzer, and a BM25 function stores the result in a hidden sparse field: the storage name of the property with `_bm25`. No other property can use that name, so the storage name of a full-text indexed property can have at most 250 characters. Records read back do not include the hidden field.
- Keyword hybrid search fuses a vector search and a BM25 search of the keywords on a full-text indexed property, both filtered, with Reciprocal Rank Fusion (k = 60). The keywords are searched as one text, analyzed like the property. Each of the two searches ranks at least 10 records, or `top` + `Skip` when more. Scores are RRF scores: a higher score ranks higher, and a score threshold is a lower bound. `GetService` returns `IKeywordHybridSearchable<TRecord>` only for a collection with a full-text indexed property.
- An array or list property can be null or empty, but its individual elements cannot be null. The provider rejects null elements before calling the SDK and identifies the record, property and element index.
- Supported metrics are cosine similarity (default), dot product similarity, Euclidean distance, and squared Euclidean distance. Scores are native Milvus scores, except Euclidean distance: the square root of the squared distance that Milvus returns. Distance thresholds are upper bounds, similarity thresholds are lower bounds.
- `IsIndexed` does not create scalar indexes; Milvus can filter scalar fields without them. Supported vector indexes are automatic (default), Flat, and HNSW.
- Field storage names must use ASCII letters, digits and underscores, starting with a letter or underscore. Reserved Milvus keywords are rejected during model construction. Varchar fields allow up to 65535 bytes. Schema changes on an existing collection require explicit management through the SDK.
- Collection names must use ASCII letters, digits and underscores, start with a letter or underscore, and have at most 255 characters: Milvus rejects other names, such as `chathistory-local`.
- For NativeAOT, use dynamic dictionary collections with explicit definitions: the typed collection APIs use reflection. Publishing with trimming or NativeAOT shows the warnings `IL2104` and `IL3053` of the dependency `Milvus.Client`.
- On .NET Framework, `Milvus.Client` calls Milvus over WinHTTP (`WinHttpHandler`, which `Grpc.Net.Client` brings in). This needs:
  - A TLS endpoint: `https: true` (the default) on the host-based registrations, or `ssl: true` on `MilvusClient`. A plaintext endpoint fails with `Response protocol downgraded to HTTP/1.1`.
  - A server certificate that Windows trusts. To validate it another way, create the `MilvusClient` on a `GrpcChannel` with a `WinHttpHandler` and pass that client to the provider.
  - Windows 11 or later. Windows Server 2019 and 2022 support gRPC over WinHTTP only partially. See [Microsoft's gRPC requirements for .NET Framework](https://learn.microsoft.com/aspnet/core/grpc/netstandard#net-framework).

  Verified on Windows 11 with .NET Framework 4.7.2 against Milvus 2.6.25 over TLS.

Host-based registrations accept `database`, `username`/`password`, or `apiKey`. The two authentication modes are mutually exclusive. Keyed variants are also available:

```csharp
builder.Services.AddMilvusVectorStore("milvus.example.com", https: true,
    database: "catalog", username: "root", password: password);
```

Host-based registrations report the selected database as `VectorStoreName` in the metadata of the store and of its collections, or `default` when none is selected. An existing `MilvusClient` does not expose the database it uses, so with a supplied client `VectorStoreName` is null. Host-based registrations own their SDK client; supplied clients remain caller-owned:

```csharp
var client = new MilvusClient("milvus.example.com", port: 19530, ssl: true,
    database: "catalog");

builder.Services.AddMilvusVectorStore(_ => client);
```
