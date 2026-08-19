// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using CommunityToolkit.VectorData.AzureCosmosDB;
using Microsoft.Shared.Diagnostics;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Extension methods to register Azure CosmosDB NoSQL <see cref="CosmosDBVectorStore"/> instances on an <see cref="IServiceCollection"/>.
/// </summary>
public static class CosmosDBServiceCollectionExtensions
{
    private const string DynamicCodeMessage = "The Cosmos NoSQL provider is currently incompatible with NativeAOT.";
    private const string UnreferencedCodeMessage = "The Cosmos NoSQL provider is currently incompatible with trimming.";

    /// <summary>
    /// Registers a <see cref="CosmosDBVectorStore"/> as <see cref="VectorStore"/>
    /// with <see cref="Database"/> retrieved from the dependency injection container.
    /// </summary>
    /// <inheritdoc cref="AddKeyedCosmosDBVectorStore(IServiceCollection, object?, CosmosDBVectorStoreOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddCosmosDBVectorStore(
        this IServiceCollection services,
        CosmosDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedCosmosDBVectorStore(services, serviceKey: null, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="CosmosDBVectorStore"/> as <see cref="VectorStore"/>
    /// with <see cref="Database"/> retrieved from the dependency injection container.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="CosmosDBVectorStore"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the vector store.</param>
    /// <param name="options">Optional options to further configure the <see cref="CosmosDBVectorStore"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedCosmosDBVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        CosmosDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Throw.IfNull(services);

        services.Add(new ServiceDescriptor(typeof(CosmosDBVectorStore), serviceKey, (sp, _) =>
        {
            var database = sp.GetRequiredService<Database>();
            options = GetStoreOptions(sp, _ => options);

            return new CosmosDBVectorStore(database, options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<CosmosDBVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="CosmosDBVectorStore"/> as <see cref="VectorStore"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <inheritdoc cref="AddKeyedCosmosDBVectorStore(IServiceCollection, object?, string, string, CosmosDBVectorStoreOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddCosmosDBVectorStore(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        CosmosDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedCosmosDBVectorStore(services, serviceKey: null, connectionString, databaseName, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="CosmosDBVectorStore"/> as <see cref="VectorStore"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="CosmosDBVectorStore"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the vector store.</param>
    /// <param name="connectionString">Connection string required to connect to Azure CosmosDB NoSQL.</param>
    /// <param name="databaseName">Database name for Azure CosmosDB NoSQL.</param>
    /// <param name="options">Optional options to further configure the <see cref="CosmosDBVectorStore"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedCosmosDBVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string connectionString,
        string databaseName,
        CosmosDBVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(connectionString);
        Throw.IfNullOrWhitespace(databaseName);

        services.Add(new ServiceDescriptor(typeof(CosmosDBVectorStore), serviceKey, (sp, _) =>
        {
            options = GetStoreOptions(sp, _ => options);
            var clientOptions = CreateClientOptions(options?.JsonSerializerOptions);

            return new CosmosDBVectorStore(connectionString, databaseName, clientOptions, options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<CosmosDBVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="CosmosDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey, TRecord}"/>
    /// with <see cref="Database"/> retrieved from the dependency injection container.
    /// </summary>
    /// <inheritdoc cref="AddKeyedCosmosDBVectorStore(IServiceCollection, object?, CosmosDBVectorStoreOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddCosmosDBCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        CosmosDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedCosmosDBCollection<TKey, TRecord>(services, serviceKey: null, name, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="CosmosDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey, TRecord}"/>
    /// with <see cref="Database"/> retrieved from the dependency injection container.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TRecord">The type of the record.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="CosmosDBCollection{TKey, TRecord}"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="options">Optional options to further configure the <see cref="CosmosDBCollection{TKey, TRecord}"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(DynamicCodeMessage)]
    [RequiresDynamicCode(UnreferencedCodeMessage)]
    public static IServiceCollection AddKeyedCosmosDBCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        CosmosDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(name);

        services.Add(new ServiceDescriptor(typeof(CosmosDBCollection<TKey, TRecord>), serviceKey, (sp, _) =>
        {
            var database = sp.GetRequiredService<Database>();
            options = GetCollectionOptions(sp, _ => options);

            return new CosmosDBCollection<TKey, TRecord>(database, name, options);
        }, lifetime));

        AddAbstractions<TKey, TRecord>(services, serviceKey, lifetime);

        return services;
    }

    /// <summary>
    /// Registers a <see cref="CosmosDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <inheritdoc cref="AddKeyedCosmosDBCollection{TKey, TRecord}(IServiceCollection, object?, string, string, string, CosmosDBCollectionOptions?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddCosmosDBCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        string connectionString,
        string databaseName,
        CosmosDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedCosmosDBCollection<TKey, TRecord>(services, serviceKey: null, name, connectionString, databaseName, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="CosmosDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionString"/> and <paramref name="databaseName"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TRecord">The type of the record.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="CosmosDBCollection{TKey, TRecord}"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="connectionString">Connection string required to connect to Azure CosmosDB NoSQL.</param>
    /// <param name="databaseName">Database name for Azure CosmosDB NoSQL.</param>
    /// <param name="options">Optional options to further configure the <see cref="CosmosDBCollection{TKey, TRecord}"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedCosmosDBCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        string connectionString,
        string databaseName,
        CosmosDBCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        Throw.IfNullOrWhitespace(connectionString);
        Throw.IfNullOrWhitespace(databaseName);

        return AddKeyedCosmosDBCollection<TKey, TRecord>(services, serviceKey, name, _ => connectionString, _ => databaseName, _ => options!, lifetime);
    }

    /// <summary>
    /// Registers a <see cref="CosmosDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionStringProvider"/> and <paramref name="databaseNameProvider"/>.
    /// </summary>
    /// <inheritdoc cref="AddKeyedCosmosDBCollection{TKey, TRecord}(IServiceCollection, object?, string, Func{IServiceProvider, string}, Func{IServiceProvider, string}, Func{IServiceProvider, CosmosDBCollectionOptions}?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddCosmosDBCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, string> connectionStringProvider,
        Func<IServiceProvider, string> databaseNameProvider,
        Func<IServiceProvider, CosmosDBCollectionOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedCosmosDBCollection<TKey, TRecord>(services, serviceKey: null, name, connectionStringProvider, databaseNameProvider, optionsProvider, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="CosmosDBCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey,TRecord}"/>
    /// using the provided <paramref name="connectionStringProvider"/> and <paramref name="databaseNameProvider"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
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
    public static IServiceCollection AddKeyedCosmosDBCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        Func<IServiceProvider, string> connectionStringProvider,
        Func<IServiceProvider, string> databaseNameProvider,
        Func<IServiceProvider, CosmosDBCollectionOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(name);
        Throw.IfNull(connectionStringProvider);
        Throw.IfNull(databaseNameProvider);

        services.Add(new ServiceDescriptor(typeof(CosmosDBCollection<TKey, TRecord>), serviceKey, (sp, _) =>
        {
            var options = GetCollectionOptions(sp, optionsProvider);
            var clientOptions = CreateClientOptions(options?.JsonSerializerOptions);

            return new CosmosDBCollection<TKey, TRecord>(
                connectionString: connectionStringProvider(sp),
                databaseName: databaseNameProvider(sp),
                name: name,
                clientOptions,
                options);
        }, lifetime));

        AddAbstractions<TKey, TRecord>(services, serviceKey, lifetime);

        return services;
    }

    private static void AddAbstractions<TKey, TRecord>(IServiceCollection services, object? serviceKey, ServiceLifetime lifetime)
        where TKey : notnull
        where TRecord : class
    {
        services.Add(new ServiceDescriptor(typeof(VectorStoreCollection<TKey, TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<CosmosDBCollection<TKey, TRecord>>(key), lifetime));

        services.Add(new ServiceDescriptor(typeof(IVectorSearchable<TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<CosmosDBCollection<TKey, TRecord>>(key), lifetime));

        services.Add(new ServiceDescriptor(typeof(IKeywordHybridSearchable<TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<CosmosDBCollection<TKey, TRecord>>(key), lifetime));
    }

    private static CosmosDBVectorStoreOptions? GetStoreOptions(IServiceProvider sp, Func<IServiceProvider, CosmosDBVectorStoreOptions?>? optionsProvider)
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

    private static CosmosDBCollectionOptions? GetCollectionOptions(IServiceProvider sp, Func<IServiceProvider, CosmosDBCollectionOptions?>? optionsProvider)
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

    private static CosmosClientOptions CreateClientOptions(JsonSerializerOptions? jsonSerializerOptions) => new()
    {
        ApplicationName = "CommunityToolkit.VectorData.AzureCosmosDB",
        UseSystemTextJsonSerializerWithOptions = jsonSerializerOptions ?? JsonSerializerOptions.Default,
    };
}
