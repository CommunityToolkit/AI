[<img src="https://raw.githubusercontent.com/dotnet-foundation/swag/master/logo/dotnetfoundation_v4.svg" alt=".NET Foundation" width=100>](https://dotnetfoundation.org)

# AI Community Toolkit

The AI Community Toolkit is a collection of .NET providers, integrations and extensions for developing AI applications with .NET.

## 📖 Documentation

Please take a look at the [Microsoft.Extensions.VectorData documentation](https://learn.microsoft.com/en-us/dotnet/ai/vector-stores/overview) on Microsoft Learn.

## 📦 NuGet Packages

The following NuGet packages have been published:

| Package | Version | Downloads |
|---------|---------|-----------|
| [CommunityToolkit.VectorData.AzureAISearch] | ![AzureAISearch Version][v-azureaisearch] | ![AzureAISearch Downloads][d-azureaisearch] |
| [CommunityToolkit.VectorData.CosmosMongoDB] | ![CosmosMongoDB Version][v-cosmosmongodb] | ![CosmosMongoDB Downloads][d-cosmosmongodb] |
| [CommunityToolkit.VectorData.CosmosNoSql] | ![CosmosNoSql Version][v-cosmosnosql] | ![CosmosNoSql Downloads][d-cosmosnosql] |
| [CommunityToolkit.VectorData.InMemory] | ![InMemory Version][v-inmemory] | ![InMemory Downloads][d-inmemory] |
| [CommunityToolkit.VectorData.PgVector] | ![PgVector Version][v-pgvector] | ![PgVector Downloads][d-pgvector] |
| [CommunityToolkit.VectorData.Qdrant] | ![Qdrant Version][v-qdrant] | ![Qdrant Downloads][d-qdrant] |
| [CommunityToolkit.VectorData.Redis] | ![Redis Version][v-redis] | ![Redis Downloads][d-redis] |
| [CommunityToolkit.VectorData.SqlServer] | ![SqlServer Version][v-sqlserver] | ![SqlServer Downloads][d-sqlserver] |
| [CommunityToolkit.VectorData.SqliteVec] | ![SqliteVec Version][v-sqlitevec] | ![SqliteVec Downloads][d-sqlitevec] |
| [CommunityToolkit.VectorData.Weaviate] | ![Weaviate Version][v-weaviate] | ![Weaviate Downloads][d-weaviate] |

## Running Live Tests

The majority of tests in the test suite use [Testcontainers](https://testcontainers.com/) so that conformance tests are run against real servers running inside Docker containers. Docker must be running on your machine for this to work.

Some providers (AzureAISearch, CosmosMongoDB) require a cloud-hosted service and cannot be tested with containers. For those, first create the required cloud service, then configure the required settings (e.g., endpoint and API key) using environment variables or the `testsettings.development.json` file in the relevant test project directory.

> **NOTE**: The `testsettings.development.json` file contains secrets and is git-ignored. It should not be checked in.

## 🚀 Contribution

We welcome community contributions. Check out our [contributing guide](CONTRIBUTING.md) to get started.

## 📄 Code of Conduct

This project has adopted the code of conduct defined by the [Contributor Covenant](https://www.contributor-covenant.org/) to clarify expected behavior in our community.
For more information, see the [Code of Conduct](CODE_OF_CONDUCT.md).

## 🏢 .NET Foundation

This project is supported by the [.NET Foundation](https://dotnetfoundation.org).

## History

The Vector Data connectors in this toolkit were initially part of the [Semantic Kernel](https://github.com/microsoft/semantic-kernel) repository. They were extracted and donated to the AI Community Toolkit to provide a vendor-neutral, community-maintained home for `Microsoft.Extensions.VectorData` providers and integrations across the .NET ecosystem.

<!-- Package links -->
[CommunityToolkit.VectorData.AzureAISearch]: https://www.nuget.org/packages/CommunityToolkit.VectorData.AzureAISearch
[CommunityToolkit.VectorData.CosmosMongoDB]: https://www.nuget.org/packages/CommunityToolkit.VectorData.CosmosMongoDB
[CommunityToolkit.VectorData.CosmosNoSql]: https://www.nuget.org/packages/CommunityToolkit.VectorData.CosmosNoSql
[CommunityToolkit.VectorData.InMemory]: https://www.nuget.org/packages/CommunityToolkit.VectorData.InMemory
[CommunityToolkit.VectorData.PgVector]: https://www.nuget.org/packages/CommunityToolkit.VectorData.PgVector
[CommunityToolkit.VectorData.Qdrant]: https://www.nuget.org/packages/CommunityToolkit.VectorData.Qdrant
[CommunityToolkit.VectorData.Redis]: https://www.nuget.org/packages/CommunityToolkit.VectorData.Redis
[CommunityToolkit.VectorData.SqlServer]: https://www.nuget.org/packages/CommunityToolkit.VectorData.SqlServer
[CommunityToolkit.VectorData.SqliteVec]: https://www.nuget.org/packages/CommunityToolkit.VectorData.SqliteVec
[CommunityToolkit.VectorData.Weaviate]: https://www.nuget.org/packages/CommunityToolkit.VectorData.Weaviate

<!-- Version badges -->
[v-azureaisearch]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.AzureAISearch
[v-cosmosmongodb]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.CosmosMongoDB
[v-cosmosnosql]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.CosmosNoSql
[v-inmemory]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.InMemory
[v-pgvector]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.PgVector
[v-qdrant]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.Qdrant
[v-redis]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.Redis
[v-sqlserver]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.SqlServer
[v-sqlitevec]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.SqliteVec
[v-weaviate]: https://badgen.net/nuget/v/CommunityToolkit.VectorData.Weaviate

<!-- Download badges -->
[d-azureaisearch]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.AzureAISearch
[d-cosmosmongodb]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.CosmosMongoDB
[d-cosmosnosql]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.CosmosNoSql
[d-inmemory]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.InMemory
[d-pgvector]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.PgVector
[d-qdrant]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.Qdrant
[d-redis]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.Redis
[d-sqlserver]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.SqlServer
[d-sqlitevec]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.SqliteVec
[d-weaviate]: https://badgen.net/nuget/dt/CommunityToolkit.VectorData.Weaviate
