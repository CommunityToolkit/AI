# CommunityToolkit.VectorData.Chroma

Chroma provider for [Microsoft.Extensions.VectorData](https://learn.microsoft.com/dotnet/ai/vector-stores/overview), by the .NET Community Toolkit.

[Chroma](https://www.trychroma.com/) is an open-source vector database for AI applications.

The provider is built on [ChromaDotNet.Client](https://www.nuget.org/packages/ChromaDotNet.Client), a .NET client for Chroma and Chroma Cloud.

## Quick start

1. Run Chroma with Docker:

```bash
docker run -d --name chroma -p 8000:8000 chromadb/chroma
```

2. Install the NuGet package:

```bash
dotnet add package CommunityToolkit.VectorData.Chroma
```

For more information, see the [Microsoft.Extensions.VectorData documentation](https://learn.microsoft.com/dotnet/ai/vector-stores/overview).

## Limitations

- On Chroma Cloud, a vector search returns at most 300 results, `Skip` included, the default quota of Chroma Cloud: beyond that, Chroma Cloud answers with a quota error.
- Chroma does not store empty lists, so an empty array or list comes back as `null`.
- Array and list properties, like the namespaces of the `TextSearchStore` of Semantic Kernel, need Chroma 1.5.0 or later.
- On self-hosted Chroma 1.0.21 to 1.5.9, a vector search can miss a record after an upsert, while a get still finds it. It is KD-49 in the [compatibility notes](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/docs/COMPATIBILITY.md#records-lost-after-an-update-of-their-embeddings) of ChromaDotNet.Client. The environment variable `RAYON_NUM_THREADS=1` on the server makes it much rarer. In the client, `ChromaUpsertStrategy.SkipUnchangedEmbeddings` writes a record whose vector does not change without its vector, so that write does not touch the vector index.
- On Chroma Cloud, a record has at most 32 metadata keys, and a null value counts as one. The provider writes every data property, null or not. A write with null values that would take a record beyond 32 keys throws an `ArgumentException`. With `WithReadsBeyondKeyQuota()`, the client reads such a record first and sends only the null values of the keys it has. Two writes of the same record at the same time can then mix.
- On Chroma Cloud, `Contains` on the full-text property throws an `ArgumentException` for a text of fewer than 3 characters. Once Chroma Cloud has indexed the records, such a filter would find no record, and its negation every record.

The connection string does not set `WithUpsertStrategy` and `WithReadsBeyondKeyQuota()`. Set them on the options of a `ChromaClient` created in code, and pass it to the vector store:

```csharp
var options = new ChromaConfigurationOptions("http://localhost:8000")
    .WithUpsertStrategy(ChromaUpsertStrategy.SkipUnchangedEmbeddings);

builder.Services.AddChromaVectorStore(_ => new ChromaClient(options));
```
