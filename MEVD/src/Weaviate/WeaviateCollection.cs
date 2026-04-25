// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Weaviate;

/// <summary>
/// Service for storing and retrieving vector records, that uses Weaviate as the underlying storage.
/// </summary>
/// <typeparam name="TKey">The data type of the record key. Must be <see cref="Guid"/>.</typeparam>
/// <typeparam name="TRecord">The data model to use for adding, updating and retrieving data from storage.</typeparam>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class WeaviateCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>, IKeywordHybridSearchable<TRecord>
    where TKey : notnull
    where TRecord : class
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>Metadata about vector store record collection.</summary>
    private readonly VectorStoreCollectionMetadata _collectionMetadata;

    /// <summary>The default options for vector search.</summary>
    private static readonly VectorSearchOptions<TRecord> s_defaultVectorSearchOptions = new();

    /// <summary>The default options for hybrid vector search.</summary>
    private static readonly HybridSearchOptions<TRecord> s_defaultKeywordVectorizedHybridSearchOptions = new();

    /// <summary><see cref="HttpClient"/> that is used to interact with Weaviate API.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>The model for this collection.</summary>
    private readonly CollectionModel _model;

    /// <summary>The mapper to use when mapping between the consumer data model and the Weaviate record.</summary>
    private readonly WeaviateMapper<TRecord> _mapper;

    /// <summary>Weaviate endpoint.</summary>
    private readonly Uri _endpoint;

    /// <summary>Weaviate API key.</summary>
    private readonly string? _apiKey;

    /// <inheritdoc />
    public override string Name { get; }

    /// <summary>Whether the vectors in the store are named and multiple vectors are supported, or whether there is just a single unnamed vector in Weaviate collection.</summary>
    private readonly bool _hasNamedVectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaviateCollection{TKey, TRecord}"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// <see cref="HttpClient"/> that is used to interact with Weaviate API.
    /// <see cref="HttpClient.BaseAddress"/> should point to remote or local cluster and API key can be configured via <see cref="HttpClient.DefaultRequestHeaders"/>.
    /// It's also possible to provide these parameters via <see cref="WeaviateCollectionOptions"/>.
    /// </param>
    /// <param name="name">The name of the collection that this <see cref="WeaviateCollection{TKey, TRecord}"/> will access.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    /// <remarks>The collection name must start with a capital letter and contain only ASCII letters and digits.</remarks>
    [RequiresUnreferencedCode("The Weaviate provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Weaviate provider is currently incompatible with NativeAOT.")]
    public WeaviateCollection(
        HttpClient httpClient,
        string name,
        WeaviateCollectionOptions? options = default)
        : this(
            httpClient,
            name,
            static options => typeof(TRecord) == typeof(Dictionary<string, object?>)
                ? throw new NotSupportedException(VectorDataStrings.NonDynamicCollectionWithDictionaryNotSupported(typeof(WeaviateDynamicCollection)))
                : new WeaviateModelBuilder(options.HasNamedVectors)
                    .Build(typeof(TRecord), typeof(TKey), options.Definition, options.EmbeddingGenerator, WeaviateConstants.s_jsonSerializerOptions),
            options)
    {
    }

    internal WeaviateCollection(HttpClient httpClient, string name, Func<WeaviateCollectionOptions, CollectionModel> modelFactory, WeaviateCollectionOptions? options)
    {
        // Verify.
        Throw.IfNull(httpClient);
        VerifyCollectionName(name);

        if (typeof(TKey) != typeof(Guid) && typeof(TKey) != typeof(object))
        {
            throw new NotSupportedException($"Only {nameof(Guid)} key is supported.");
        }

        var endpoint = (options?.Endpoint ?? httpClient.BaseAddress) ?? throw new ArgumentException($"Weaviate endpoint should be provided via HttpClient.BaseAddress property or {nameof(WeaviateCollectionOptions)} options parameter.");

        options ??= WeaviateCollectionOptions.Default;

        // Assign.
        _httpClient = httpClient;
        _endpoint = endpoint;
        Name = name;
        _model = modelFactory(options);
        _apiKey = options.ApiKey;
        _hasNamedVectors = options.HasNamedVectors;

        // Assign mapper.
        _mapper = new WeaviateMapper<TRecord>(Name, options.HasNamedVectors, _model, WeaviateConstants.s_jsonSerializerOptions);

        _collectionMetadata = new()
        {
            VectorStoreSystemName = WeaviateConstants.VectorStoreSystemName,
            CollectionName = name
        };
    }

    /// <inheritdoc />
    public override async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new WeaviateGetCollectionSchemaRequest(Name).Build();

        var response = await 
            ExecuteRequestWithNotFoundHandlingAsync<WeaviateGetCollectionSchemaResponse>(request, cancellationToken)
            .ConfigureAwait(false);

        return response != null;
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        // Don't even try to create if the collection already exists.
        if (await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var schema = WeaviateCollectionCreateMapping.MapToSchema(
            Name,
            _hasNamedVectors,
            _model);

        using var request = new WeaviateCreateCollectionSchemaRequest(schema).Build();

        try
        {
            await ExecuteRequestAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (VectorStoreException)
        {
            // Since weaviate error info is ambiguous, we can check here if the index already exists.
            // If it does, we can ignore the error.
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                if (await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch
            {
            }
#pragma warning restore CA1031 // Do not catch general exception types

            throw;
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        using var request = new WeaviateDeleteCollectionSchemaRequest(Name).Build();

        await ExecuteRequestAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        var guid = key switch
        {
            Guid g => g,
            object o => (Guid)o,
            _ => throw new UnreachableException("Guid key should have been validated during model building")
        };

        using var request = new WeaviateDeleteObjectRequest(Name, guid).Build();

        await ExecuteRequestAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        const string ContainsAnyOperator = "ContainsAny";

        Throw.IfNull(keys);

        var stringKeys = keys.Select(key => key.ToString()).ToList();

        if (stringKeys.Count == 0)
        {
            return;
        }

        var match = new WeaviateQueryMatch
        {
            CollectionName = Name,
            WhereClause = new WeaviateQueryMatchWhereClause
            {
                Operator = ContainsAnyOperator,
                Path = [WeaviateConstants.ReservedKeyPropertyName],
                Values = stringKeys!
            }
        };

        using var request = new WeaviateDeleteObjectBatchRequest(match).Build();
        await ExecuteRequestAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        var guid = key as Guid? ?? throw new InvalidCastException("Only Guid keys are supported");
        var includeVectors = options?.IncludeVectors is true;
        if (includeVectors && _model.EmbeddingGenerationRequired)
        {
            throw new NotSupportedException(VectorDataStrings.IncludeVectorsNotSupportedWithEmbeddingGeneration);
        }

        using var request = new WeaviateGetCollectionObjectRequest(Name, guid, includeVectors).Build();

        var jsonObject = await ExecuteRequestWithNotFoundHandlingAsync<JsonObject>(request, cancellationToken).ConfigureAwait(false);

        if (jsonObject is null)
        {
            return default;
        }

        return _mapper.MapFromStorageToDataModel(jsonObject!, includeVectors);
    }

    /// <inheritdoc />
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
        => UpsertAsync([record], cancellationToken);

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(records);

        IReadOnlyList<TRecord>? recordsList = null;

        // If an embedding generator is defined, invoke it once per property for all records.
        IReadOnlyList<Embedding>?[]? generatedEmbeddings = null;

        var vectorPropertyCount = _model.VectorProperties.Count;
        for (var i = 0; i < vectorPropertyCount; i++)
        {
            var vectorProperty = _model.VectorProperties[i];

            if (WeaviateModelBuilder.IsVectorPropertyTypeValidCore(vectorProperty.Type, out _))
            {
                continue;
            }

            // We have a vector property whose type isn't natively supported - we need to generate embeddings.
            Debug.Assert(vectorProperty.EmbeddingGenerator is not null);

            // We have a property with embedding generation; materialize the records' enumerable if needed, to
            // prevent multiple enumeration.
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
            generatedEmbeddings ??= new IReadOnlyList<Embedding>?[vectorPropertyCount];
            generatedEmbeddings[i] = await vectorProperty.GenerateEmbeddingsAsync(records.Select(r => vectorProperty.GetValueAsObject(r)), cancellationToken).ConfigureAwait(false);
        }

        var keyProperty = _model.KeyProperty;
        var jsonObjects = new List<JsonObject>();
        var recordIndex = 0;
        foreach (var record in records)
        {
            if (keyProperty.IsAutoGenerated && keyProperty.GetValue<Guid>(record) == Guid.Empty)
            {
                keyProperty.SetValue(record, Guid.NewGuid());
            }

            jsonObjects.Add(_mapper.MapFromDataToStorageModel(record, recordIndex++, generatedEmbeddings));
        }

        if (jsonObjects.Count == 0)
        {
            return;
        }

        using var request = new WeaviateUpsertCollectionObjectBatchRequest(jsonObjects).Build();

        await ExecuteRequestAsync<List<WeaviateUpsertCollectionObjectBatchResponse>>(request, cancellationToken).ConfigureAwait(false);
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
        var vector = await GetSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);

        var query = WeaviateQueryBuilder.BuildSearchQuery(
            vector,
            Name,
            vectorProperty.StorageName,
            WeaviateConstants.s_jsonSerializerOptions,
            top,
            options,
            _model,
            _hasNamedVectors);

        await foreach (var record in ExecuteQueryAsync(query, options.IncludeVectors, WeaviateConstants.ScorePropertyName, operationName: "VectorSearch", cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    private static async ValueTask<ReadOnlyMemory<float>> GetSearchVectorAsync<TInput>(TInput searchValue, VectorPropertyModel vectorProperty, CancellationToken cancellationToken)
        where TInput : notnull
        => searchValue switch
        {
            ReadOnlyMemory<float> r => r,
            float[] f => new ReadOnlyMemory<float>(f),
            Embedding<float> e => e.Vector,
            _ when vectorProperty.EmbeddingGenerationDispatcher is not null
                => ((Embedding<float>)await vectorProperty.GenerateEmbeddingAsync(searchValue, cancellationToken).ConfigureAwait(false)).Vector,

            _ => vectorProperty.EmbeddingGenerator is null
                ? throw new NotSupportedException(VectorDataStrings.InvalidSearchInputAndNoEmbeddingGeneratorWasConfigured(searchValue.GetType(), WeaviateModelBuilder.SupportedVectorTypes))
                : throw new InvalidOperationException(VectorDataStrings.IncompatibleEmbeddingGeneratorWasConfiguredForInputType(typeof(TInput), vectorProperty.EmbeddingGenerator.GetType()))
        };

    #endregion Search

    /// <inheritdoc />
    public override IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(filter);
        Throw.IfLessThan(top, 1);

        options ??= new();

        var query = WeaviateQueryBuilder.BuildQuery(
            filter,
            top,
            options,
            Name,
            _model,
            _hasNamedVectors);

        return ExecuteQueryAsync(query, options.IncludeVectors, WeaviateConstants.ScorePropertyName, "GetAsync", cancellationToken)
            .Select(result => result.Record);
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
        const string OperationName = "HybridSearch";

        Throw.IfLessThan(top, 1);

        options ??= s_defaultKeywordVectorizedHybridSearchOptions;
        var vectorProperty = _model.GetVectorPropertyOrSingle<TRecord>(new() { VectorProperty = options.VectorProperty });
        var vector = await GetSearchVectorAsync(searchValue, vectorProperty, cancellationToken).ConfigureAwait(false);
        var textDataProperty = _model.GetFullTextDataPropertyOrSingle(options.AdditionalProperty);

        var query = WeaviateQueryBuilder.BuildHybridSearchQuery(
            vector,
            top,
            string.Join(" ", keywords),
            Name,
            _model,
            vectorProperty,
            textDataProperty,
            WeaviateConstants.s_jsonSerializerOptions,
            options,
            _hasNamedVectors);

        await foreach (var record in ExecuteQueryAsync(query, options.IncludeVectors, WeaviateConstants.HybridScorePropertyName, OperationName, cancellationToken).ConfigureAwait(false))
        {
            // Note that unlike regular vector search, Weaviate hybrid search does not support 'certainty' (filtering via score threshold).
            // So we filter client-side here instead.
            if (options.ScoreThreshold.HasValue && record.Score < options.ScoreThreshold.Value)
            {
                continue;
            }

            yield return record;
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);

        return
            serviceKey is not null ? null :
            serviceType == typeof(VectorStoreCollectionMetadata) ? _collectionMetadata :
            serviceType == typeof(HttpClient) ? _httpClient :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    #region private

    private async IAsyncEnumerable<VectorSearchResult<TRecord>> ExecuteQueryAsync(string query, bool includeVectors, string scorePropertyName, string operationName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new WeaviateVectorSearchRequest(query).Build();

        var (responseModel, content) = await ExecuteRequestWithResponseContentAsync<WeaviateVectorSearchResponse>(request, cancellationToken).ConfigureAwait(false);

        var collectionResults = responseModel?.Data?.GetOperation?[Name];

        if (collectionResults is null)
        {
            throw new VectorStoreException($"Error occurred during vector search. Response: {content}")
            {
                VectorStoreSystemName = WeaviateConstants.VectorStoreSystemName,
                VectorStoreName = _collectionMetadata.VectorStoreName,
                CollectionName = Name,
                OperationName = operationName
            };
        }

        foreach (var result in collectionResults)
        {
            if (result is not null)
            {
                var (storageModel, score) = WeaviateCollectionSearchMapping.MapSearchResult(result, scorePropertyName, _hasNamedVectors);

                var record = _mapper.MapFromStorageToDataModel(storageModel, includeVectors);

                yield return new VectorSearchResult<TRecord>(record, score);
            }
        }
    }

    private Task<HttpResponseMessage> ExecuteRequestAsync(
        HttpRequestMessage request,
        bool ensureSuccessStatusCode = true,
        CancellationToken cancellationToken = default)
    {
        request.RequestUri = new Uri(_endpoint, request.RequestUri!);

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        return VectorStoreErrorHandler.RunOperationAsync<HttpResponseMessage, HttpRequestException>(
            _collectionMetadata,
            $"{request.Method} {request.RequestUri}",
            async () =>
            {
                var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                if (ensureSuccessStatusCode)
                {
                    response.EnsureSuccessStatusCode();
                }

                return response;
            });
    }

    private async Task<(TResponse?, string)> ExecuteRequestWithResponseContentAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await ExecuteRequestAsync(request, ensureSuccessStatusCode: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(
#if NET
            cancellationToken
#endif
        ).ConfigureAwait(false);

        var responseModel = VectorStoreErrorHandler.RunOperation<TResponse?, JsonException>(
            _collectionMetadata,
            $"{request.Method} {request.RequestUri}",
            () => JsonSerializer.Deserialize<TResponse>(responseContent, WeaviateConstants.s_jsonSerializerOptions));

        return (responseModel, responseContent);
    }

    private async Task<TResponse?> ExecuteRequestAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (model, _) = await ExecuteRequestWithResponseContentAsync<TResponse>(request, cancellationToken).ConfigureAwait(false);

        return model;
    }

    private async Task<TResponse?> ExecuteRequestWithNotFoundHandlingAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await ExecuteRequestAsync(request, ensureSuccessStatusCode: false, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(
#if NET
            cancellationToken
#endif
        ).ConfigureAwait(false);

        var responseModel = VectorStoreErrorHandler.RunOperation<TResponse?, JsonException>(
            _collectionMetadata,
            $"{request.Method} {request.RequestUri}",
            () => JsonSerializer.Deserialize<TResponse>(responseContent, WeaviateConstants.s_jsonSerializerOptions));

        return responseModel;
    }

    private static void VerifyCollectionName(string collectionName)
    {
        Throw.IfNullOrWhitespace(collectionName);

        // Based on https://weaviate.io/developers/weaviate/starter-guides/managing-collections#collection--property-names
        char first = collectionName[0];
        if (first is not (>= 'A' and <= 'Z'))
        {
            throw new ArgumentException("Collection name must start with an uppercase ASCII letter.", nameof(collectionName));
        }

        foreach (char character in collectionName)
        {
            if (character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            {
                throw new ArgumentException("Collection name must contain only ASCII letters and digits or underscores. The first character must be an upper case letter.", nameof(collectionName));
            }
        }
    }
    #endregion
}
