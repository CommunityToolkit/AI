// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Npgsql;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.PgVector;

/// <summary>
/// Represents a vector store implementation using PostgreSQL.
/// </summary>
public sealed class PostgresVectorStore : VectorStore
{
    /// <summary>Metadata about vector store.</summary>
    private readonly VectorStoreMetadata _metadata;

    /// <summary>A general purpose definition that can be used to construct a collection when needing to proxy schema agnostic operations.</summary>
    private static readonly VectorStoreCollectionDefinition s_generalPurposeDefinition = new() { Properties = [new VectorStoreKeyProperty("Key", typeof(string))] };

    /// <summary>Data source used to interact with the database.</summary>
    private readonly NpgsqlDataSource _dataSource;
    private readonly NpgsqlDataSourceArc? _dataSourceArc;
    private readonly string _databaseName;

    /// <summary>The database schema.</summary>
    private readonly string? _schema;

    private readonly IEmbeddingGenerator? _embeddingGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVectorStore"/> class.
    /// </summary>
    /// <param name="dataSource">Postgres data source.</param>
    /// <param name="ownsDataSource">A value indicating whether <paramref name="dataSource"/> is disposed when this instance of <see cref="PostgresVectorStore"/> is disposed.</param>
    /// <param name="options">Optional configuration options for this class</param>
    public PostgresVectorStore(NpgsqlDataSource dataSource, bool ownsDataSource, PostgresVectorStoreOptions? options = default)
    {
        Throw.IfNull(dataSource);

        _schema = options?.Schema;
        _embeddingGenerator = options?.EmbeddingGenerator;
        _dataSource = dataSource;
        _dataSourceArc = ownsDataSource ? new NpgsqlDataSourceArc(dataSource) : null;
        _databaseName = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).Database!;

        _metadata = new()
        {
            VectorStoreSystemName = PostgresConstants.VectorStoreSystemName,
            VectorStoreName = _databaseName
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVectorStore"/> class.
    /// </summary>
    /// <param name="connectionString">Postgres database connection string.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    public PostgresVectorStore(string connectionString, PostgresVectorStoreOptions? options = default)
#pragma warning disable CA2000 // Dispose objects before losing scope
        : this(PostgresUtils.CreateDataSource(connectionString), ownsDataSource: true, options)
#pragma warning restore CA2000 // Dispose objects before losing scope
    {
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _dataSourceArc?.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<string> ListCollectionNamesAsync(CancellationToken cancellationToken = default)
    {
        return PostgresUtils.WrapAsyncEnumerableAsync(
            GetTablesAsync(cancellationToken),
            "ListCollectionNames",
            _metadata);

        async IAsyncEnumerable<string> GetTablesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using (connection)
            using (var command = connection.CreateCommand())
            {
                PostgresSqlBuilder.BuildGetTablesCommand(command, _schema);
                using NpgsqlDataReader dataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return dataReader.GetString(dataReader.GetOrdinal("table_name"));
                }
            }
        }
    }

#pragma warning disable IDE0090 // Use 'new(...)'
    /// <inheritdoc />
    [RequiresDynamicCode("This overload of GetCollection() is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
    [RequiresUnreferencedCode("This overload of GetCollecttion() is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, call GetDynamicCollection() instead.")]
#if NET
    public override PostgresCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#else
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#endif
        => typeof(TRecord) == typeof(Dictionary<string, object?>)
            ? throw new ArgumentException(VectorDataStrings.GetCollectionWithDictionaryNotSupported)
            : new PostgresCollection<TKey, TRecord>(
                _dataSource,
                _dataSourceArc,
                name,
                new()
                {
                    Schema = _schema,
                    Definition = definition,
                    EmbeddingGenerator = _embeddingGenerator,
                }
            );

    /// <inheritdoc />
#if NET
    public override PostgresDynamicCollection GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#else
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#endif
        => new PostgresDynamicCollection(
            _dataSource,
            _dataSourceArc,
            name,
            new()
            {
                Schema = _schema,
                Definition = definition,
                EmbeddingGenerator = _embeddingGenerator,
            }
        );
#pragma warning restore IDE0090 // Use 'new(...)'

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var collection = GetDynamicCollection(name, s_generalPurposeDefinition);
        return collection.CollectionExistsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        var collection = GetDynamicCollection(name, s_generalPurposeDefinition);
        return collection.EnsureCollectionDeletedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreMetadata) ? _metadata :
            serviceType == typeof(NpgsqlDataSource) ? _dataSource :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }
}
