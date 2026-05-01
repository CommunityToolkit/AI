# CommunityToolkit.VectorData.Qdrant

Qdrant provider for [Microsoft.Extensions.VectorData](https://learn.microsoft.com/dotnet/ai/vector-stores/overview), by the .NET Community Toolkit.

[Qdrant](https://qdrant.tech/) is an open-source vector similarity search engine that provides fast and scalable vector search with filtering support.

## Quick start

1. Run Qdrant with Docker:

```bash
docker run -d --name qdrant -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

2. Install the NuGet package:

```bash
dotnet add package CommunityToolkit.VectorData.Qdrant
```

For more information, see the [Microsoft.Extensions.VectorData documentation](https://learn.microsoft.com/dotnet/ai/vector-stores/overview).
