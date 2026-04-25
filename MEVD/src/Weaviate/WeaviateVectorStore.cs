// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Weaviate;

/// <summary>
/// Class for accessing the list of collections in a Weaviate vector store.
/// </summary>
/// <remarks>
/// This class can be used with collections of any schema type, but requires you to provide schema information when getting a collection.
/// </remarks>
public sealed class WeaviateVectorStore : VectorStore
{
    /// <summary>Metadata about vector store.</summary>
    private readonly VectorStoreMetadata _metadata;

    /// <summary><see cref="HttpClient"/> that is used to interact with Weaviate API.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>A general purpose definition that can be used to construct a collection when needing to proxy schema agnostic operations.</summary>
    private static readonly VectorStoreCollectionDefinition s_generalPurposeDefinition = new() { Properties = [new VectorStoreKeyProperty("Key", typeof(Guid)), new VectorStoreVectorProperty("Vector", typeof(ReadOnlyMemory<float>), 1)] };

    /// <summary>Weaviate endpoint for remote or local cluster.</summary>
    private readonly Uri? _endpoint;

    /// <summary>
    /// Weaviate API key.
    /// </summary>
    private readonly string? _apiKey;

    /// <summary>Whether the vectors in the store are named and multiple vectors are supported, or whether there is just a single unnamed vector in Weaviate collection.</summary>
    private readonly bool _hasNamedVectors;

    private readonly IEmbeddingGenerator? _embeddingGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaviateVectorStore"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// <see cref="HttpClient"/> that is used to interact with Weaviate API.
    /// <see cref="HttpClient.BaseAddress"/> should point to remote or local cluster and API key can be configured via <see cref="HttpClient.DefaultRequestHeaders"/>.
    /// It's also possible to provide these parameters via <see cref="WeaviateVectorStoreOptions"/>.
    /// </param>
    /// <param name="options">Optional configuration options for this class.</param>
    [RequiresUnreferencedCode("The Weaviate provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Weaviate provider is currently incompatible with NativeAOT.")]
    public WeaviateVectorStore(HttpClient httpClient, WeaviateVectorStoreOptions? options = null)
    {
        Throw.IfNull(httpClient);

        _httpClient = httpClient;

        options ??= WeaviateVectorStoreOptions.Default;
        _endpoint = options.Endpoint;
        _apiKey = options.ApiKey;
        _hasNamedVectors = options.HasNamedVectors;
        _embeddingGenerator = options.EmbeddingGenerator;

        _metadata = new()
        {
            VectorStoreSystemName = WeaviateConstants.VectorStoreSystemName
        };
    }

#pragma warning disable IDE0090 // Use 'new(...)'
    /// <inheritdoc />
    /// <remarks>The collection name must start with a capital letter and contain only ASCII letters and digits.</remarks>
    [RequiresUnreferencedCode("The Weaviate provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Weaviate provider is currently incompatible with NativeAOT.")]
#if NET
    public override WeaviateCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#else
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
#endif
        => typeof(TRecord) == typeof(Dictionary<string, object?>)
            ? throw new ArgumentException(VectorDataStrings.GetCollectionWithDictionaryNotSupported)
            : new WeaviateCollection<TKey, TRecord>(
                _httpClient,
                name,
                new()
                {
                    Definition = definition,
                    Endpoint = _endpoint,
                    ApiKey = _apiKey,
                    HasNamedVectors = _hasNamedVectors,
                    EmbeddingGenerator = _embeddingGenerator
                });

    /// <inheritdoc />
    // TODO: The provider uses unsafe JSON serialization in many places, #11963
    [RequiresUnreferencedCode("The Weaviate provider is currently incompatible with trimming.")]
    [RequiresDynamicCode("The Weaviate provider is currently incompatible with NativeAOT.")]
#if NET
    public override WeaviateDynamicCollection GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#else
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
#endif
        => new WeaviateDynamicCollection(
            _httpClient,
            name,
            new()
            {
                Definition = definition,
                Endpoint = _endpoint,
                ApiKey = _apiKey,
                HasNamedVectors = _hasNamedVectors,
                EmbeddingGenerator = _embeddingGenerator
            });
#pragma warning restore IDE0090

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string OperationName = "ListCollectionNames";

        using var request = new WeaviateGetCollectionsRequest().Build();

        var httpResponseContent = await VectorStoreErrorHandler.RunOperationAsync<string, HttpRequestException>(
            _metadata,
            OperationName,
            async () =>
            {
                var httpResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);

                httpResponse.EnsureSuccessStatusCode();

                return await httpResponse.Content.ReadAsStringAsync(
#if NET
                    cancellationToken
#endif
                ).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var collectionsResponse = VectorStoreErrorHandler.RunOperation<WeaviateGetCollectionsResponse?, JsonException>(
            _metadata,
            OperationName,
            () => JsonSerializer.Deserialize<WeaviateGetCollectionsResponse>(httpResponseContent));

        if (collectionsResponse?.Collections is not null)
        {
            foreach (var collection in collectionsResponse.Collections)
            {
                yield return collection.CollectionName;
            }
        }
    }

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
            serviceType == typeof(HttpClient) ? _httpClient :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }
}
