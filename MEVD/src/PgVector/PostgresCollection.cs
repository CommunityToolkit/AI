// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Npgsql;
using Pgvector;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.PgVector;

/// <summary>
/// Represents a collection of vector store records in a Postgres database.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TRecord">The type of the record.</typeparam>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class PostgresCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>, IKeywordHybridSearchable<TRecord>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    where TKey : notnull
    where TRecord : class
{
    /// <inheritdoc />
    public override string Name { get; }

    /// <summary>Metadata about vector store record collection.</summary>
    private readonly VectorStoreCollectionMetadata _collectionMetadata;

    /// <summary>Data source used to interact with the database.</summary>
    private readonly NpgsqlDataSource _dataSource;
    private readonly NpgsqlDataSourceArc? _dataSourceArc;
    private readonly string _databaseName;

    /// <summary>The database schema.</summary>
    private readonly string? _schema;

    /// <summary>The model for this collection.</summary>
    private readonly CollectionModel _model;

    /// <summary>A mapper to use for converting between the data model and the Azure AI Search record.</summary>
    private readonly PostgresMapper<TRecord> _mapper;

    /// <summary>The default options for vector search.</summary>
    private static readonly VectorSearchOptions<TRecord> s_defaultVectorSearchOptions = new();

    /// <summary>The default options for hybrid search.</summary>
    private static readonly HybridSearchOptions<TRecord> s_defaultHybridSearchOptions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="dataSource">The data source to use for connecting to the database.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="ownsDataSource">A value indicating whether <paramref name="dataSource"/> is disposed when the collection is disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate PostgresDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate PostgresDynamicCollection instead")]
    public PostgresCollection(NpgsqlDataSource dataSource, string name, bool ownsDataSource, PostgresCollectionOptions? options = default)
        : this(dataSource, ownsDataSource ? new NpgsqlDataSourceArc(dataSource) : null, name, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="connectionString">Postgres database connection string.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate PostgresDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate PostgresDynamicCollection instead")]
    public PostgresCollection(string connectionString, string name, PostgresCollectionOptions? options = default)
        : this(PostgresUtils.CreateDataSource(connectionString), name, ownsDataSource: true, options)
    {
        Throw.IfNullOrWhitespace(connectionString);
    }

    [RequiresDynamicCode("This constructor is incompatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, instantiate PostgresDynamicCollection instead.")]
    [RequiresUnreferencedCode("This constructor is incompatible with trimming. For dynamic mapping via Dictionary<string, object?>, instantiate PostgresDynamicCollection instead.")]
    internal PostgresCollection(NpgsqlDataSource dataSource, NpgsqlDataSourceArc? dataSourceArc, string name, PostgresCollectionOptions? options)
        : this(
            dataSource,
            dataSourceArc,
            name,
            static options => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(PostgresDynamicCollection)))
                : new PostgresModelBuilder().Build(typeof(TRecord), typeof(TKey), options.Definition, options.EmbeddingGenerator),
            options)
    {
    }

    internal PostgresCollection(NpgsqlDataSource dataSource, NpgsqlDataSourceArc? dataSourceArc, string name, Func<PostgresCollectionOptions, CollectionModel> modelFactory, PostgresCollectionOptions? options)
    {
        Throw.IfNullOrWhitespace(name);

        options ??= PostgresCollectionOptions.Default;

        Name = name;
        _model = modelFactory(options);
        _mapper = new PostgresMapper<TRecord>(_model);

        _dataSource = dataSource;
        _dataSourceArc = dataSourceArc;
        _databaseName = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).Database!;
        _schema = options.Schema;

        _collectionMetadata = new()
        {
            VectorStoreSystemName = PostgresConstants.VectorStoreSystemName,
            VectorStoreName = _databaseName,
            CollectionName = name
        };

        // Don't add any lines after this - an exception thrown afterwards would leave the reference count wrongly incremented.
        _dataSourceArc?.IncrementReferenceCount();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _dataSourceArc?.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => RunOperationAsync("DoesTableExists", async () =>
        {
            NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using (connection)
            using (var command = connection.CreateCommand())
            {
                PostgresSqlBuilder.BuildDoesTableExistCommand(command, _schema, Name);
                using NpgsqlDataReader dataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                if (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return dataReader.GetString(dataReader.GetOrdinal("table_name")) == Name;
                }

                return false;
            }
        });

    /// <inheritdoc/>
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        return RunOperationAsync("EnsureCollectionExists", () =>
            InternalCreateCollectionAsync(true, cancellationToken));
    }

    /// <inheritdoc/>
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildDropTableCommand(command, _schema, Name);

        await RunOperationAsync("DeleteCollection", () => command.ExecuteNonQueryAsync()).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
        => UpsertAsync([record], cancellationToken);

    /// <inheritdoc/>
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(records);
        IReadOnlyList<TRecord>? recordsList = null;

        // If an embedding generator is defined, invoke it once per property for all records.
        Dictionary<VectorPropertyModel, IReadOnlyList<Embedding>>? generatedEmbeddings = null;

        var vectorPropertyCount = _model.VectorProperties.Count;
        for (var i = 0; i < vectorPropertyCount; i++)
        {
            var vectorProperty = _model.VectorProperties[i];

            if (PostgresModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
            {
                continue;
            }

            // We have a vector property whose type isn't natively supported - we need to generate embeddings.
            Debug.Assert(vectorProperty.EmbeddingGenerator is not null);

            // Materialize the records' enumerable if needed, to prevent multiple enumeration.
            if (recordsList is null)
            {
                recordsList = records is IReadOnlyList<TRecord> r ? r : records.ToList();

                if (recordsList.Count == 0)
                {
                    return;
                }

                records = recordsList;
            }

            // TODO: Ideally we'd group together vector properties using the same generator (and with the same input and output properties),
            // and generate embeddings for them in a single batch. That's some more complexity though.
            generatedEmbeddings ??= new Dictionary<VectorPropertyModel, IReadOnlyList<Embedding>>(vectorPropertyCount);
            generatedEmbeddings[vectorProperty] = await vectorProperty.GenerateEmbeddingsAsync(records.Select(r => vectorProperty.GetValueAsObject(r)), cancellationToken).ConfigureAwait(false);
        }

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var batch = connection.CreateBatch();

        // If key auto-generation is enabled, we'll need to enumerate over the records multiple times:
        // once to generate the upsert batch, and again to populate the retrieved keys into the records.
        // To prevent multiple enumeration of the input enumerable, we materialize it here if needed.
        if (_model.KeyProperty.IsAutoGenerated && recordsList is null)
        {
            recordsList = records is IReadOnlyList<TRecord> r ? r : records.ToList();

            if (recordsList.Count == 0)
            {
                return;
            }

            records = recordsList;
        }

        if (PostgresSqlBuilder.BuildUpsertCommand<TKey>(batch, _schema, Name, _model, records, generatedEmbeddings))
        {
            await RunOperationAsync("Upsert", async () =>
            {
                var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var keyProperty = _model.KeyProperty;
                    if (keyProperty.IsAutoGenerated)
                    {
                        int? keyOrdinal = null;

                        foreach (var record in recordsList!)
                        {
                            if (Equals(keyProperty.GetValueAsObject(record), default(TKey)))
                            {
                                keyOrdinal ??= reader.GetOrdinal(keyProperty.StorageName);

                                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                                var keyValue = reader.GetFieldValue<TKey>(keyOrdinal.Value);
                                _model.KeyProperty.SetValue<TKey>(record, keyValue);
                                await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                }
                finally
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(key);

        bool includeVectors = options?.IncludeVectors is true;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildGetCommand(command, _schema, Name, _model, key, includeVectors);

        return await connection.ExecuteWithErrorHandlingAsync(
            _collectionMetadata,
            operationName: "Get",
            async () =>
            {
                using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                return reader.HasRows
                    ? _mapper.MapFromStorageToDataModel(reader, includeVectors)
                    : null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string OperationName = "GetBatch";

        Throw.IfNull(keys);

        bool includeVectors = options?.IncludeVectors is true;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        List<TKey> listOfKeys = keys.ToList();
        if (listOfKeys.Count == 0)
        {
            yield break;
        }

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildGetBatchCommand(command, _schema, Name, _model, listOfKeys, includeVectors);

        using var reader = await connection.ExecuteWithErrorHandlingAsync(
            _collectionMetadata,
            OperationName,
            () => command.ExecuteReaderAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        while (await reader.ReadWithErrorHandlingAsync(_collectionMetadata, OperationName, cancellationToken).ConfigureAwait(false))
        {
            yield return _mapper.MapFromStorageToDataModel(reader, includeVectors);
        }
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildDeleteCommand(command, _schema, Name, _model.KeyProperty.StorageName, key);

        await RunOperationAsync("Delete", () => command.ExecuteNonQueryAsync(cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(keys);

        var listOfKeys = keys.ToList();
        if (listOfKeys.Count == 0)
        {
            return;
        }

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildDeleteBatchCommand(command, _schema, Name, _model.KeyProperty, listOfKeys);

        await RunOperationAsync("DeleteBatch", () => command.ExecuteNonQueryAsync(cancellationToken)).ConfigureAwait(false);
    }

    #region Search

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(searchValue);
        Throw.IfLessThan(top, 1);

        options ??= s_defaultVectorSearchOptions;
        if (options.IncludeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var vectorProperty = _model.GetVectorPropertyOrSingle(options);
        var pgVector = await ConvertSearchInputToVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        // Simulating skip/offset logic locally, since OFFSET can work only with LIMIT in combination
        // and LIMIT is not supported in vector search extension, instead of LIMIT - "k" parameter is used.
        var limit = top + options.Skip;

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildGetNearestMatchCommand(command, _schema, Name, _model, vectorProperty, pgVector,
            options.Filter, options.Skip, options.IncludeVectors, top, options.ScoreThreshold);

        using var reader = await connection.ExecuteWithErrorHandlingAsync(
            _collectionMetadata,
            "Search",
            () => command.ExecuteReaderAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        while (await reader.ReadWithErrorHandlingAsync(_collectionMetadata, "Search", cancellationToken).ConfigureAwait(false))
        {
            yield return new VectorSearchResult<TRecord>(
                _mapper.MapFromStorageToDataModel(reader, options.IncludeVectors),
                reader.GetDouble(reader.GetOrdinal(PostgresConstants.DistanceColumnName)));
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<VectorSearchResult<TRecord>> HybridSearchAsync<TInput>(
        TInput searchValue,
        ICollection<string> keywords,
        int top,
        HybridSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        Throw.IfNull(searchValue);
        Throw.IfNull(keywords);
        Throw.IfLessThan(top, 1);

        options ??= s_defaultHybridSearchOptions;
        if (options.IncludeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        var vectorProperty = _model.GetVectorPropertyOrSingle<TRecord>(new() { VectorProperty = options.VectorProperty });
        var textProperty = _model.GetFullTextDataPropertyOrSingle(options.AdditionalProperty);
        var pgVector = await ConvertSearchInputToVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildHybridSearchCommand(command, _schema, Name, _model, vectorProperty, textProperty, pgVector, keywords,
            options.Filter, options.Skip, options.IncludeVectors, top, options.ScoreThreshold);

        using var reader = await connection.ExecuteWithErrorHandlingAsync(
            _collectionMetadata,
            "HybridSearch",
            () => command.ExecuteReaderAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        while (await reader.ReadWithErrorHandlingAsync(_collectionMetadata, "HybridSearch", cancellationToken).ConfigureAwait(false))
        {
            yield return new VectorSearchResult<TRecord>(
                _mapper.MapFromStorageToDataModel(reader, options.IncludeVectors),
                reader.GetDouble(reader.GetOrdinal(PostgresConstants.DistanceColumnName)));
        }
    }

    #endregion Search

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(filter);
        Throw.IfLessThan(top, 1);

        options ??= new();

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        PostgresSqlBuilder.BuildSelectWhereCommand(command, _schema, Name, _model, filter, top, options);
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadWithErrorHandlingAsync(_collectionMetadata, "Get", cancellationToken).ConfigureAwait(false))
        {
            yield return _mapper.MapFromStorageToDataModel(reader, options.IncludeVectors);
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreCollectionMetadata) ? _collectionMetadata :
            serviceType == typeof(NpgsqlDataSource) ? _dataSource :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    private async Task InternalCreateCollectionAsync(bool ifNotExists, CancellationToken cancellationToken = default)
    {
        // Execute the commands in a transaction.
        NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            var pgVersion = connection.PostgreSqlVersion;

            // Prepare the SQL commands.
            using var batch = connection.CreateBatch();

            // First, check if the pgvector extension is already installed in PostgreSQL, and then install it if not.
            // Note that we do a separate check before doing CREATE EXTENSION IF EXISTS in order to know if it was actually created,
            // since in that case we must also must call ReloadTypesAsync() at the Npgsql level
            batch.BatchCommands.Add(new NpgsqlBatchCommand("SELECT EXISTS(SELECT * FROM pg_extension WHERE extname='vector')"));
            batch.BatchCommands.Add(new NpgsqlBatchCommand("CREATE EXTENSION IF NOT EXISTS vector"));

            bool extensionAlreadyExisted;

            try
            {
                extensionAlreadyExisted = (bool)(await batch.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // CREATE EXTENSION IF NOT EXISTS is not atomic in PG, so concurrent sessions doing this at the same time
                // may trigger a unique constraint violation. We ignore it and interpret it to mean that the extension
                // already exists.
                extensionAlreadyExisted = true;
            }

            if (!extensionAlreadyExisted)
            {
                await connection.ReloadTypesAsync().ConfigureAwait(false);
            }

            batch.BatchCommands.Clear();

            // Now create the table and any indexes.
            // Note that this must be done in a separate batch/transaction as the creation of the extension above.
            batch.BatchCommands.Add(
                new NpgsqlBatchCommand(PostgresSqlBuilder.BuildCreateTableSql(_schema, Name, _model, pgVersion, ifNotExists)));

            foreach (var (column, kind, function, isVector, isFullText, fullTextLanguage) in PostgresPropertyMapping.GetIndexInfo(_model.Properties))
            {
                batch.BatchCommands.Add(
                    new NpgsqlBatchCommand(
                        PostgresSqlBuilder.BuildCreateIndexSql(_schema, Name, column, kind, function, isVector, isFullText, fullTextLanguage, ifNotExists)));
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task RunOperationAsync(string operationName, Func<Task> operation)
        => VectorStoreErrorHandler.RunOperationAsync<NpgsqlException>(
            _collectionMetadata,
            operationName,
            operation);

    private Task<T> RunOperationAsync<T>(string operationName, Func<Task<T>> operation)
        => VectorStoreErrorHandler.RunOperationAsync<T, NpgsqlException>(
            _collectionMetadata,
            operationName,
            operation);

    /// <summary>
    /// Converts a search input value to a PostgreSQL vector representation, generating embeddings if necessary.
    /// </summary>
    private async Task<object> ConvertSearchInputToVectorAsync<TInput>(TInput searchValue, VectorPropertyModel vectorProperty, CancellationToken cancellationToken)
        where TInput : notnull
    {
        object vector = searchValue switch
        {
            // Dense float32
            ReadOnlyMemory<float> r => r,
            float[] f => new ReadOnlyMemory<float>(f),
            Embedding<float> e => e.Vector,

#if NET
            // Dense float16
            ReadOnlyMemory<Half> r => r,
            Half[] f => new ReadOnlyMemory<Half>(f),
            Embedding<Half> e => e.Vector,
#endif

            // Dense Binary
            BitArray b => b,
            BinaryEmbedding e => e.Vector,

            // Sparse
            SparseVector sv => sv,
            // TODO: Add a PG-specific SparseVectorEmbedding type

            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false),

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), PostgresModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

        var pgVector = PostgresPropertyMapping.MapVectorForStorageModel(vector);
        Throw.IfNull(pgVector);
        return pgVector;
    }
}
