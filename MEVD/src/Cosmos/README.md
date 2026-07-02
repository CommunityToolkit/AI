# CommunityToolkit.VectorData.Cosmos

`CommunityToolkit.VectorData.Cosmos` is the Azure Cosmos DB for NoSQL provider for `Microsoft.Extensions.VectorData`.

## Install

```xml
<PackageReference Include="CommunityToolkit.VectorData.Cosmos" Version="1.0.0-preview.1" />
```

## Get started

```csharp
using Microsoft.Azure.Cosmos;
using CommunityToolkit.VectorData.Cosmos;
using Microsoft.Extensions.VectorData;

CosmosClient client = new("<connection-string>");
Database database = client.GetDatabase("vector-database");

VectorStore vectorStore = new CosmosNoSqlVectorStore(database);
VectorStoreCollection<string, Hotel> collection =
    vectorStore.GetCollection<string, Hotel>("hotels");
```

You can also register the provider with dependency injection:

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddCosmosNoSqlVectorStore("<connection-string>", "vector-database");
services.AddCosmosNoSqlCollection<string, Hotel>(
    name: "hotels",
    connectionString: "<connection-string>",
    databaseName: "vector-database");
```

## Define a record model

```csharp
using Microsoft.Extensions.VectorData;

public sealed class Hotel
{
    [VectorStoreKey]
    public string HotelId { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string HotelName { get; set; } = string.Empty;

    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> DescriptionEmbedding { get; set; }
}
```

## Create and query a collection

```csharp
await collection.EnsureCollectionExistsAsync();

await collection.UpsertAsync(new Hotel
{
    HotelId = "hotel-1",
    HotelName = "Contoso Suites",
    DescriptionEmbedding = embedding
});

await foreach (VectorSearchResult<Hotel> result in collection.SearchAsync(embedding, top: 3))
{
    Console.WriteLine($"{result.Record.HotelName}: {result.Score}");
}
```
