// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using CommunityToolkit.VectorData.Milvus;
using Milvus.Client;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to register <see cref="MilvusVectorStore"/> and <see cref="MilvusCollection{TKey, TRecord}"/>  instances on an <see cref="IServiceCollection"/>.
/// </summary>
public static class MilvusServiceCollectionExtensions
{
    private const string DynamicCodeMessage = "This method is incompatible with NativeAOT, consult the documentation for adding collections in a way that's compatible with NativeAOT.";
    private const string UnreferencedCodeMessage = "This method is incompatible with trimming, consult the documentation for adding collections in a way that's compatible with NativeAOT.";

    /// <summary>
    /// Registers a <see cref="MilvusVectorStore"/> as <see cref="VectorStore"/>
    /// with <see cref="MilvusClient"/> returned by <paramref name="clientProvider"/>
    /// or retrieved from the dependency injection container if <paramref name="clientProvider"/> was not provided.
    /// </summary>
    /// <inheritdoc cref="AddKeyedMilvusVectorStore(IServiceCollection, object?, Func{IServiceProvider, MilvusClient}, Func{IServiceProvider, MilvusVectorStoreOptions}?, ServiceLifetime)"/>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusVectorStore(
        this IServiceCollection services,
        Func<IServiceProvider, MilvusClient>? clientProvider = default,
        Func<IServiceProvider, MilvusVectorStoreOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedMilvusVectorStore(services, serviceKey: null, clientProvider, optionsProvider, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusVectorStore"/> as <see cref="VectorStore"/>
    /// with <see cref="MilvusClient"/> returned by <paramref name="clientProvider"/> or retrieved from the dependency injection container if <paramref name="clientProvider"/> was not provided.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="MilvusVectorStore"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the vector store.</param>
    /// <param name="clientProvider">The <see cref="MilvusClient"/> provider.</param>
    /// <param name="optionsProvider">Options provider to further configure the <see cref="MilvusVectorStore"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, MilvusClient>? clientProvider = default,
        Func<IServiceProvider, MilvusVectorStoreOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedMilvusVectorStoreCore(services, serviceKey, clientProvider, optionsProvider, lifetime, ownsClient: false);

    private static IServiceCollection AddKeyedMilvusVectorStoreCore(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, MilvusClient>? clientProvider,
        Func<IServiceProvider, MilvusVectorStoreOptions?>? optionsProvider,
        ServiceLifetime lifetime, bool ownsClient, string? databaseName = null)
    {
        Throw.IfNull(services);

        services.Add(new ServiceDescriptor(typeof(MilvusVectorStore), serviceKey, (sp, _) =>
        {
            var client = clientProvider is null ? sp.GetRequiredService<MilvusClient>() : clientProvider(sp);
            var options = GetStoreOptions(sp, optionsProvider);

            // Clients created by host overloads are owned, and their database is known; supplied clients remain caller-owned.
            return new MilvusVectorStore(new ClientWrapper(client, ownsClient, databaseName), options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<MilvusVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="MilvusVectorStore"/> using a host without authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusVectorStore(
        this IServiceCollection services,
        string host,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedMilvusVectorStore(services, serviceKey: null, host, port, https, database, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusVectorStore"/> using a host without authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key with which to associate the service.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string host,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Throw.IfNullOrWhitespace(host);

        return AddKeyedMilvusVectorStoreCore(services, serviceKey, _ => new MilvusClient(host, port, https, database), _ => options,
            lifetime, ownsClient: true, databaseName: database ?? "default");
    }

    /// <summary>
    /// Registers a <see cref="MilvusVectorStore"/> using a host and API-key authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="apiKey">The API key to use for authentication.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusVectorStore(
        this IServiceCollection services,
        string host,
        string apiKey,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedMilvusVectorStore(services, serviceKey: null, host, apiKey, port, https, database, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusVectorStore"/> using a host and API-key authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key with which to associate the service.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="apiKey">The API key to use for authentication.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string host,
        string apiKey,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Throw.IfNullOrWhitespace(host);
        Throw.IfNullOrWhitespace(apiKey);

        return AddKeyedMilvusVectorStoreCore(services, serviceKey, _ => new MilvusClient(host, apiKey, port, https, database), _ => options,
            lifetime, ownsClient: true, databaseName: database ?? "default");
    }

    /// <summary>
    /// Registers a <see cref="MilvusVectorStore"/> using a host and username/password authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="username">The authentication username.</param>
    /// <param name="password">The authentication password.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusVectorStore(
        this IServiceCollection services,
        string host,
        string username,
        string password,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => AddKeyedMilvusVectorStore(services, serviceKey: null, host, username, password, port, https, database, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusVectorStore"/> using a host and username/password authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key with which to associate the service.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="username">The authentication username.</param>
    /// <param name="password">The authentication password.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string host,
        string username,
        string password,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusVectorStoreOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Throw.IfNullOrWhitespace(host);
        Throw.IfNullOrWhitespace(username);
        Throw.IfNullOrWhitespace(password);

        return AddKeyedMilvusVectorStoreCore(services, serviceKey, _ => new MilvusClient(host, username, password, port, https, database), _ => options,
            lifetime, ownsClient: true, databaseName: database ?? "default");
    }

    /// <summary>
    /// Registers a <see cref="MilvusCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey, TRecord}"/>
    /// with <see cref="MilvusClient"/> returned by <paramref name="clientProvider"/> or retrieved from the dependency injection container if <paramref name="clientProvider"/> was not provided.
    /// </summary>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, MilvusClient>? clientProvider = default,
        Func<IServiceProvider, MilvusCollectionOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedMilvusCollection<TKey, TRecord>(services, serviceKey: null, name, clientProvider, optionsProvider, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusCollection{TKey, TRecord}"/> as <see cref="VectorStoreCollection{TKey, TRecord}"/>
    /// with <see cref="MilvusClient"/> returned by <paramref name="clientProvider"/> or retrieved from the dependency injection container if <paramref name="clientProvider"/> was not provided.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="MilvusCollection{TKey, TRecord}"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="clientProvider">The <see cref="MilvusClient"/> provider.</param>
    /// <param name="optionsProvider">Options provider to further configure the <see cref="MilvusCollection{TKey, TRecord}"/>.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        Func<IServiceProvider, MilvusClient>? clientProvider = default,
        Func<IServiceProvider, MilvusCollectionOptions>? optionsProvider = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedMilvusCollectionCore<TKey, TRecord>(services, serviceKey, name, clientProvider, optionsProvider, lifetime, ownsClient: false);

    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    private static IServiceCollection AddKeyedMilvusCollectionCore<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        Func<IServiceProvider, MilvusClient>? clientProvider,
        Func<IServiceProvider, MilvusCollectionOptions?>? optionsProvider,
        ServiceLifetime lifetime, bool ownsClient, string? databaseName = null)
        where TKey : notnull
        where TRecord : class
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(name);

        services.Add(new ServiceDescriptor(typeof(MilvusCollection<TKey, TRecord>), serviceKey, (sp, _) =>
        {
            var client = clientProvider is null ? sp.GetRequiredService<MilvusClient>() : clientProvider(sp);
            var options = GetCollectionOptions(sp, optionsProvider);

            // Clients created by host overloads are owned, and their database is known; supplied clients remain caller-owned.
            return new MilvusCollection<TKey, TRecord>(() => new ClientWrapper(client, ownsClient, databaseName), name, options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStoreCollection<TKey, TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<MilvusCollection<TKey, TRecord>>(key), lifetime));

        services.Add(new ServiceDescriptor(typeof(IVectorSearchable<TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<MilvusCollection<TKey, TRecord>>(key), lifetime));

        services.Add(new ServiceDescriptor(typeof(IKeywordHybridSearchable<TRecord>), serviceKey,
            static (sp, key) => sp.GetRequiredKeyedService<MilvusCollection<TKey, TRecord>>(key), lifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="MilvusCollection{TKey, TRecord}"/> using a host without authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        string host,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedMilvusCollection<TKey, TRecord>(services, serviceKey: null, name, host, port, https, database, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusCollection{TKey, TRecord}"/> using a host without authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key with which to associate the service.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        string host,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        Throw.IfNullOrWhitespace(host);

        return AddKeyedMilvusCollectionCore<TKey, TRecord>(services, serviceKey, name, _ => new MilvusClient(host, port, https, database), _ => options,
            lifetime, ownsClient: true, databaseName: database ?? "default");
    }

    /// <summary>
    /// Registers a <see cref="MilvusCollection{TKey, TRecord}"/> using a host and API-key authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="apiKey">The API key to use for authentication.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        string host,
        string apiKey,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedMilvusCollection<TKey, TRecord>(services, serviceKey: null, name, host, apiKey, port, https, database, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusCollection{TKey, TRecord}"/> using a host and API-key authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key with which to associate the service.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="apiKey">The API key to use for authentication.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        string host,
        string apiKey,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        Throw.IfNullOrWhitespace(host);
        Throw.IfNullOrWhitespace(apiKey);

        return AddKeyedMilvusCollectionCore<TKey, TRecord>(services, serviceKey, name, _ => new MilvusClient(host, apiKey, port, https, database), _ => options,
            lifetime, ownsClient: true, databaseName: database ?? "default");
    }

    /// <summary>
    /// Registers a <see cref="MilvusCollection{TKey, TRecord}"/> using a host and username/password authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="username">The authentication username.</param>
    /// <param name="password">The authentication password.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        string host,
        string username,
        string password,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
        => AddKeyedMilvusCollection<TKey, TRecord>(services, serviceKey: null, name, host, username, password, port, https, database, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="MilvusCollection{TKey, TRecord}"/> using a host and username/password authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key with which to associate the service.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="username">The authentication username.</param>
    /// <param name="password">The authentication password.</param>
    /// <param name="port">The port to connect to. Defaults to 19530.</param>
    /// <param name="https">Whether to encrypt the connection using HTTPS. Defaults to <c>false</c>.</param>
    /// <param name="database">The database to connect to, or null for the default database.</param>
    /// <param name="options">Options to further configure the service.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>Service collection.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public static IServiceCollection AddKeyedMilvusCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        string host,
        string username,
        string password,
        int port = MilvusConstants.DefaultPort,
        bool https = false,
        string? database = null,
        MilvusCollectionOptions? options = default,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        Throw.IfNullOrWhitespace(host);
        Throw.IfNullOrWhitespace(username);
        Throw.IfNullOrWhitespace(password);

        return AddKeyedMilvusCollectionCore<TKey, TRecord>(services, serviceKey, name, _ => new MilvusClient(host, username, password, port, https, database), _ => options,
            lifetime, ownsClient: true, databaseName: database ?? "default");
    }

    private static MilvusVectorStoreOptions? GetStoreOptions(IServiceProvider sp, Func<IServiceProvider, MilvusVectorStoreOptions?>? optionsProvider)
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

    private static MilvusCollectionOptions? GetCollectionOptions(IServiceProvider sp, Func<IServiceProvider, MilvusCollectionOptions?>? optionsProvider)
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
}
