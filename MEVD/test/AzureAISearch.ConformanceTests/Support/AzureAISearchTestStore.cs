// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq.Expressions;
using Azure;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Humanizer;
using Microsoft.Extensions.VectorData;
using CommunityToolkit.VectorData.AzureAISearch;
using VectorData.ConformanceTests.Support;

namespace AzureAISearch.ConformanceTests.Support;

internal sealed class AzureAISearchTestStore : TestStore
{
    public static AzureAISearchTestStore Instance { get; } = new();

    private SearchIndexClient? _client;

    // Track all collection names created through this TestStore, so we can clean them up in StopAsync.
    // This is needed because Azure AI Search free/basic tiers have a low index limit (e.g. 3),
    // so we must delete indexes between test classes.
    private readonly HashSet<string> _trackedCollectionNames = new();

    public SearchIndexClient Client
        => this._client ?? throw new InvalidOperationException("Call InitializeAsync() first");

    public AzureAISearchVectorStore GetVectorStore(AzureAISearchVectorStoreOptions options)
        => new(this.Client, options);

    private AzureAISearchTestStore()
    {
    }

    public override VectorStoreCollection<TKey, TRecord> CreateCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition definition)
    {
        _trackedCollectionNames.Add(name);
        return base.CreateCollection<TKey, TRecord>(name, definition);
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> CreateDynamicCollection(
        string name,
        VectorStoreCollectionDefinition definition)
    {
        _trackedCollectionNames.Add(name);
        return base.CreateDynamicCollection(name, definition);
    }

    protected override Task StartAsync()
    {
        (string? serviceUrl, string? apiKey) = (AzureAISearchTestEnvironment.ServiceUrl, AzureAISearchTestEnvironment.ApiKey);

        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            throw new InvalidOperationException("Service URL is not configured, set AzureAISearch:ServiceUrl (and AzureAISearch:ApiKey if you want)");
        }

        this._client = string.IsNullOrWhiteSpace(apiKey)
            ? new SearchIndexClient(new Uri(serviceUrl), new DefaultAzureCredential())
            : new SearchIndexClient(new Uri(serviceUrl), new AzureKeyCredential(apiKey!));

        this.DefaultVectorStore = new AzureAISearchVectorStore(this._client);

        return Task.CompletedTask;
    }

    protected override async Task StopAsync()
    {
        // Delete all tracked indexes to stay within the Azure AI Search index limit.
        // With parallelism disabled, StopAsync is called between each test class (refcount goes 1→0).
        foreach (var name in _trackedCollectionNames)
        {
            try
            {
                await Client.DeleteIndexAsync(name);
            }
            catch
            {
                // Best-effort cleanup; ignore failures (e.g. index already deleted by the test)
            }
        }

        _trackedCollectionNames.Clear();
    }

    // Azure AI search only supports lowercase letters, digits or dashes.
    // Also, add a suffix containing machine name to allow multiple developers to work against the same cloud instance.
    public override string AdjustCollectionName(string baseName)
        => baseName.Kebaberize() + AzureAISearchTestEnvironment.TestIndexPostfix;

    public override async Task WaitForDataAsync<TKey, TRecord>(
        VectorStoreCollection<TKey, TRecord> collection,
        int recordCount,
        Expression<Func<TRecord, bool>>? filter = null,
        Expression<Func<TRecord, object?>>? vectorProperty = null,
        int? vectorSize = null,
        object? dummyVector = null)
    {
        await base.WaitForDataAsync(collection, recordCount, filter, vectorProperty, vectorSize, dummyVector);

        // There seems to be some asynchronicity/race condition specific to Azure AI Search which isn't taken care
        // of by the generic retry loop in the base implementation.
        // TODO: Investigate this and remove
        await Task.Delay(TimeSpan.FromMilliseconds(1000));
    }
}
