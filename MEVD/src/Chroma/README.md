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
