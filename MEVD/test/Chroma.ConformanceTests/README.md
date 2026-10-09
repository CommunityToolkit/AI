# Chroma Vector Store Conformance Tests

This project contains conformance tests for the Chroma Vector Store implementation.

## Running the Tests

By default, the tests will automatically use a testcontainer to spin up a Chroma instance. Docker must be running on your machine for this to work.

The hybrid search tests need Chroma Cloud, the only Chroma with the sparse vector indexes that hybrid search uses: they run only when `Chroma:ConnectionString` has a Chroma Cloud address, like `https://api.trychroma.com`, and are skipped otherwise.

### Using an External Chroma Instance

If you want to run the tests against an external Chroma instance (e.g., Chroma Cloud, a local Chroma server, or any other instance), you can provide a connection string through one of the following methods:

#### Option 1: Environment Variable

Set the `Chroma__ConnectionString` environment variable:

```bash
# Bash/Linux/macOS
export Chroma__ConnectionString="Endpoint=https://api.trychroma.com;Token=ck-...;Tenant=my-tenant;Database=my-test-database"

# PowerShell
$env:Chroma__ConnectionString = "Endpoint=https://api.trychroma.com;Token=ck-...;Tenant=my-tenant;Database=my-test-database"
```

#### Option 2: Configuration File

Create a `testsettings.development.json` file in this directory with the following content:

```json
{
  "Chroma": {
    "ConnectionString": "Endpoint=https://api.trychroma.com;Token=ck-...;Tenant=my-tenant;Database=my-test-database"
  }
}
```

This file is git-ignored and safe for local development.

#### Option 3: User Secrets

```bash
cd MEVD/test/Chroma.ConformanceTests
dotnet user-secrets set "Chroma:ConnectionString" "Endpoint=https://api.trychroma.com;Token=ck-...;Tenant=my-tenant;Database=my-test-database"
```

## Benefits of Using an External Instance

Using an external Chroma instance can be beneficial when:
- You want to avoid the overhead of spinning up Docker containers
- You need to test against Chroma Cloud specifically
- You want faster test execution (no container startup time)
- You're running tests in an environment where Docker is not available
