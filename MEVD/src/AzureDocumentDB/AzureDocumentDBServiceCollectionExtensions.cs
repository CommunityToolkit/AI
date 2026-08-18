// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using CommunityToolkit.VectorData.AzureDocumentDB;
using MongoDB.Driver;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to register Azure DocumentDB <see cref="VectorStore"/> instances on an <see cref="IServiceCollection"/>.
/// </summary>
public static class AzureDocumentDBServiceCollectionExtensions
{
    private const string DynamicCodeMessage = "This method is incompatible with NativeAOT, consult the documentation for adding collections in a way that's compatible with NativeAOT.";
    private const string UnreferencedCodeMessage = "This method is incompatible with trimming, consult the documentation for adding collections in a way that's compatible with NativeAOT.";
    private const string ApplicationId = "CommunityToolkit.MEVD";

    /// <summary>
    /// Registers a <see cref="AzureDocumentDBVectorStore"/> as <see cref="VectorStore"/>
    /// with <see cref="IMongoDatabase"/> retrieved from the dependency injection container.
    /// </summary>
    /// <inheritdoc cref="AddKeyedAzureDocumentDBVectorStore(IServiceCollection, object?, AzureDocumentDBVectorStoreOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddAzureDocumentDBVectorStore(
        this IServiceCollection services,
        AzureDocumentDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedAzureDocumentDBVectorStore(services, serviceKey: null, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="AzureDocumentDBVectorStore"/> as <see cref="VectorStore"/>
    /// with <see cref="IMongoDatabase"/> retrieved from the dependency injection container.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="AzureDocumentDBVectorStore"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the vector store.</param>
    /// <param name="options">Optional options to further configure the <see cref="AzureDocumentDBVectorStore"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedAzureDocumentDBVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        AzureDocumentDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Throw.IfNull(services);

        services.Add(new ServiceDescriptor(typeof(AzureDocumentDBVectorStore), serviceKey, (sp, _) =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            options = GetStoreOptions(sp, _ => options);

            return new AzureDocumentDBVectorStore(database, options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<AzureDocumentDBVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="AzureDocumentDBVectorStore"/> as <see cref="VectorStore"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <inheritdoc cref="AddKeyedAzureDocumentDBVectorStore(IServiceCollection, object?, string, string, AzureDocumentDBVectorStoreOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddAzureDocumentDBVectorStore(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        AzureDocumentDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedAzureDocumentDBVectorStore(services, serviceKey: null, connectionString, databaseName, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="AzureDocumentDBVectorStore"/> as <see cref="VectorStore"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="AzureDocumentDBVectorStore"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the vector store.</param>
    /// <param name="connectionString">Connection string required to connect to Azure DocumentDB.</param>
    /// <param name="databaseName">Database name for Azure DocumentDB.</param>
    /// <param name="options">Optional options to further configure the <see cref="AzureDocumentDBVectorStore"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedAzureDocumentDBVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string connectionString,
        string databaseName,
        AzureDocumentDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(connectionString);
        Throw.IfNullOrWhitespace(databaseName);

        services.Add(new ServiceDescriptor(typeof(AzureDocumentDBVectorStore), serviceKey, (sp, _) =>
        {
            options = GetStoreOptions(sp, _ => options);
            MongoClient mongoClient = new(CreateClientSettings(connectionString));
            var database = mongoClient.GetDatabase(databaseName);

            return new AzureDocumentDBVectorStore(database, options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<AzureDocumentDBVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey, TRecord}"/>
    /// with <see cref="IMongoDatabase"/> retrieved from the dependency injection container.
    /// </summary>
    /// <inheritdoc cref="AddKeyedAzureDocumentDBVectorStore(IServiceCollection, object?, AzureDocumentDBVectorStoreOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddAzureDocumentDBCollection<TRecord>(
        this IServiceCollection services,
        string name,
        AzureDocumentDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRecord : class
        => AddKeyedAzureDocumentDBCollection<TRecord>(services, serviceKey: null, name, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey, TRecord}"/>
    /// with <see cref="IMongoDatabase"/> retrieved from the dependency injection container.
    /// </summary>
    /// <typeparam name="TRecord">The type of the record.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="options">Optional options to further configure the <see cref="AzureDocumentDBCollection{TKey, TRecord}"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedAzureDocumentDBCollection<TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        AzureDocumentDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRecord : class
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(name);

        services.Add(new ServiceDescriptor(typeof(AzureDocumentDBCollection<string, TRecord>), serviceKey, (sp, _) =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            options = GetCollectionOptions(sp, _ => options);

            return new AzureDocumentDBCollection<string, TRecord>(database, name, options);
        }, lifetime));

        AddAbstractions<string, TRecord>(services, serviceKey, lifetime);

        return services;
    }

    /// <summary>
    /// Registers a <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <inheritdoc cref="AddKeyedAzureDocumentDBCollection{TRecord}(IServiceCollection, object?, string, string, string, AzureDocumentDBCollectionOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddAzureDocumentDBCollection<TRecord>(
        this IServiceCollection services,
        string name,
        string connectionString,
        string databaseName,
        AzureDocumentDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRecord : class
        => AddKeyedAzureDocumentDBCollection<TRecord>(services, serviceKey: null, name, connectionString, databaseName, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <typeparam name="TRecord">The type of the record.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="connectionString">Connection string required to connect to Azure DocumentDB.</param>
    /// <param name="databaseName">Database name for Azure DocumentDB.</param>
    /// <param name="options">Optional options to further configure the <see cref="AzureDocumentDBCollection{TKey, TRecord}"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedAzureDocumentDBCollection<TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        string connectionString,
        string databaseName,
        AzureDocumentDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRecord : class
    {
        Throw.IfNullOrWhitespace(connectionString);
        Throw.IfNullOrWhitespace(databaseName);

        return AddKeyedAzureDocumentDBCollection<TRecord>(services, serviceKey, name, _ => connectionString, _ => databaseName, _ => options!, lifetime);
    }

    /// <summary>
    /// Registers a <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionStringProvider"/> and <paramref name="databaseNameProvider"/>.
    /// </summary>
    /// <inheritdoc cref="AddKeyedAzureDocumentDBCollection{TRecord}(IServiceCollection, object?, string, Func{IServiceProvider, string}, Func{IServiceProvider, string}, Func{IServiceProvider, AzureDocumentDBCollectionOptions}?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddAzureDocumentDBCollection<TRecord>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, string> connectionStringProvider,
        Func<IServiceProvider, string> databaseNameProvider,
        Func<IServiceProvider, AzureDocumentDBCollectionOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRecord : class
        => AddKeyedAzureDocumentDBCollection<TRecord>(services, serviceKey: null, name, connectionStringProvider, databaseNameProvider, optionsProvider, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="AzureDocumentDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionStringProvider"/> and <paramref name="databaseNameProvider"/>.
    /// </summary>
    /// <typeparam name="TRecord">The type of the record.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="VectorStoreCollection{TKey, TRecord}"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="connectionStringProvider">The connection string provider.</param>
    /// <param name="databaseNameProvider">The database name provider.</param>
    /// <param name="optionsProvider">Options provider to further configure the collection.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedAzureDocumentDBCollection<TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        Func<IServiceProvider, string> connectionStringProvider,
        Func<IServiceProvider, string> databaseNameProvider,
        Func<IServiceProvider, AzureDocumentDBCollectionOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRecord : class
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(name);
        Throw.IfNull(connectionStringProvider);
        Throw.IfNull(databaseNameProvider);

        services.Add(new ServiceDescriptor(typeof(AzureDocumentDBCollection<string, TRecord>), serviceKey, (sp, _) =>
        {
            var options = GetCollectionOptions(sp, optionsProvider);
            MongoClient mongoClient = new(CreateClientSettings(connectionStringProvider(sp)));
            var database = mongoClient.GetDatabase(databaseNameProvider(sp));

            return new AzureDocumentDBCollection<string, TRecord>(database, name, options);
        }, lifetime));

        AddAbstractions<string, TRecord>(services, serviceKey, lifetime);

        return services;
    }

    private static void AddAbstractions<TKey, TRecord>(IServiceCollection services, object? serviceKey, ServiceLifetime lifetime)
        where TKey : notnull
        where TRecord : class
    {
        services.Add(new ServiceDescriptor(typeof(VectorStoreCollection<TKey, TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<AzureDocumentDBCollection<TKey, TRecord>>(key), lifetime));

        services.Add(new ServiceDescriptor(typeof(IVectorSearchable<TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<AzureDocumentDBCollection<TKey, TRecord>>(key), lifetime));

        // Once HybridSearch supports get implemented by AzureDocumentDBCollection,
        // we need to add IKeywordHybridSearchable abstraction here as well.
    }

    private static AzureDocumentDBVectorStoreOptions? GetStoreOptions(IServiceProvider sp, Func<IServiceProvider, AzureDocumentDBVectorStoreOptions?>? optionsProvider)
    {
        var options = optionsProvider?.Invoke(sp);
        if (options?.EmbeddingGenerator is not null)
        {
            return options; // The user has provided everything, there is nothing to change.
        }

        var embeddingGenerator = sp.GetService<IEmbeddingGenerator>();
        return embeddingGenerator is null
            ? options // There is nothing to change.
            : new(options) { EmbeddingGenerator = embeddingGenerator }; // Create a brand new copy in order to avoid modifying the original options.
    }

    private static AzureDocumentDBCollectionOptions? GetCollectionOptions(IServiceProvider sp, Func<IServiceProvider, AzureDocumentDBCollectionOptions?>? optionsProvider)
    {
        var options = optionsProvider?.Invoke(sp);
        if (options?.EmbeddingGenerator is not null)
        {
            return options; // The user has provided everything, there is nothing to change.
        }

        var embeddingGenerator = sp.GetService<IEmbeddingGenerator>();
        return embeddingGenerator is null
            ? options // There is nothing to change.
            : new(options) { EmbeddingGenerator = embeddingGenerator }; // Create a brand new copy in order to avoid modifying the original options.
    }

    private static MongoClientSettings CreateClientSettings(string connectionString)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.ApplicationName = ApplicationId;
        return settings;
    }
}
