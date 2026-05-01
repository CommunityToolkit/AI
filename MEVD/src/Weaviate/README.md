# CommunityToolkit.VectorData.Weaviate

Weaviate provider for [Microsoft.Extensions.VectorData](https://learn.microsoft.com/dotnet/ai/vector-stores/overview), by the .NET Community Toolkit.

[Weaviate](https://weaviate.io/) is an open-source vector database that supports vector search, hybrid search, and generative search capabilities.

## Quick start

1. Run Weaviate with Docker:

```bash
docker run -d --name weaviate -p 8080:8080 -p 50051:50051 cr.weaviate.io/semitechnologies/weaviate
```

2. Install the NuGet package:

```bash
dotnet add package CommunityToolkit.VectorData.Weaviate
```

For more information, see the [Microsoft.Extensions.VectorData documentation](https://learn.microsoft.com/dotnet/ai/vector-stores/overview).
