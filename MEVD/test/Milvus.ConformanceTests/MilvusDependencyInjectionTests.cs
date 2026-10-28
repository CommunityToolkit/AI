// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.VectorData.Milvus;
using Milvus.Client;
using VectorData.ConformanceTests;
using Xunit;

namespace Milvus.ConformanceTests;

public class MilvusDependencyInjectionTests
    : DependencyInjectionTests<MilvusVectorStore, MilvusCollection<long, DependencyInjectionTests<long>.Record>, long, DependencyInjectionTests<long>.Record>
{
    private const string Host = "localhost";
    private const int Port = 8080;
    private const string ApiKey = "fakeKey";

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey = null)
        => configuration.AddInMemoryCollection(
        [
            new(CreateConfigKey("Milvus", serviceKey, "Host"), Host),
            new(CreateConfigKey("Milvus", serviceKey, "Port"), Port.ToString()),
            new(CreateConfigKey("Milvus", serviceKey, "ApiKey"), ApiKey),
        ]);

    private static string HostProvider(IServiceProvider sp, object? serviceKey = null)
        => sp.GetRequiredService<IConfiguration>().GetRequiredSection(CreateConfigKey("Milvus", serviceKey, "Host")).Value!;

    private static int PortProvider(IServiceProvider sp, object? serviceKey = null)
        => int.Parse(sp.GetRequiredService<IConfiguration>().GetRequiredSection(CreateConfigKey("Milvus", serviceKey, "Port")).Value!);

    private static string ApiKeyProvider(IServiceProvider sp, object? serviceKey = null)
        => sp.GetRequiredService<IConfiguration>().GetRequiredSection(CreateConfigKey("Milvus", serviceKey, "ApiKey")).Value!;

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates
    {
        get
        {
            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services
                    .AddSingleton<MilvusClient>(sp => new MilvusClient(Host, ApiKey, Port))
                    .AddMilvusCollection<long, Record>(name, lifetime: lifetime)
                : services
                    .AddSingleton<MilvusClient>(sp => new MilvusClient(Host, ApiKey, Port))
                    .AddKeyedMilvusCollection<long, Record>(serviceKey, name, lifetime: lifetime);

            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddMilvusCollection<long, Record>(
                    name, Host, ApiKey, Port, lifetime: lifetime)
                : services.AddKeyedMilvusCollection<long, Record>(
                    serviceKey, name, Host, ApiKey, Port, lifetime: lifetime);

            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddMilvusCollection<long, Record>(
                    name, sp => new MilvusClient(HostProvider(sp), ApiKeyProvider(sp), PortProvider(sp)), lifetime: lifetime)
                : services.AddKeyedMilvusCollection<long, Record>(
                    serviceKey, name, sp => new MilvusClient(HostProvider(sp, serviceKey), ApiKeyProvider(sp, serviceKey), PortProvider(sp, serviceKey)), lifetime: lifetime);
        }
    }

    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates
    {
        get
        {
            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddMilvusVectorStore(
                    Host, ApiKey, Port, lifetime: lifetime)
                : services.AddKeyedMilvusVectorStore(
                    serviceKey, Host, ApiKey, Port, lifetime: lifetime);

            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services
                    .AddSingleton<MilvusClient>(sp => new MilvusClient(Host, ApiKey, Port))
                    .AddMilvusVectorStore(lifetime: lifetime)
                : services
                    .AddSingleton<MilvusClient>(sp => new MilvusClient(Host, ApiKey, Port))
                    .AddKeyedMilvusVectorStore(serviceKey, lifetime: lifetime);
        }
    }

    [Fact]
    public void HostCantBeNullOrEmpty()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddMilvusVectorStore(host: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedMilvusVectorStore(serviceKey: "notNull", host: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddMilvusCollection<long, Record>(
            name: "notNull", host: null!));
        Assert.Throws<ArgumentException>(() => services.AddMilvusCollection<long, Record>(
            name: "notNull", host: ""));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedMilvusCollection<long, Record>(
            serviceKey: "notNull", name: "notNull", host: null!));
        Assert.Throws<ArgumentException>(() => services.AddKeyedMilvusCollection<long, Record>(
            serviceKey: "notNull", name: "notNull", host: ""));
    }
}
