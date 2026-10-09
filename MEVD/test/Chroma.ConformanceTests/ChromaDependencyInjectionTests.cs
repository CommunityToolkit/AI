// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ChromaDB.Client;
using CommunityToolkit.VectorData.Chroma;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VectorData.ConformanceTests;
using Xunit;

namespace Chroma.ConformanceTests;

public class ChromaDependencyInjectionTests
    : DependencyInjectionTests<ChromaVectorStore, ChromaCollection<string, DependencyInjectionTests<string>.Record>, string, DependencyInjectionTests<string>.Record>
{
    private const string ConnectionString = "Endpoint=http://localhost:8000;Token=fakeToken";

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey = null)
        => configuration.AddInMemoryCollection(
        [
            new(CreateConfigKey("Chroma", serviceKey, "ConnectionString"), ConnectionString),
        ]);

    private static string ConnectionStringProvider(IServiceProvider sp, object? serviceKey = null)
        => sp.GetRequiredService<IConfiguration>().GetRequiredSection(CreateConfigKey("Chroma", serviceKey, "ConnectionString")).Value!;

    private static ChromaClient CreateClient(string connectionString)
        => new(ChromaConfigurationOptions.FromConnectionString(connectionString));

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates
    {
        get
        {
            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services
                    .AddSingleton(sp => CreateClient(ConnectionString))
                    .AddChromaCollection<string, Record>(name, lifetime: lifetime)
                : services
                    .AddSingleton(sp => CreateClient(ConnectionString))
                    .AddKeyedChromaCollection<string, Record>(serviceKey, name, lifetime: lifetime);

            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddChromaCollection<string, Record>(name, ConnectionString, lifetime: lifetime)
                : services.AddKeyedChromaCollection<string, Record>(serviceKey, name, ConnectionString, lifetime: lifetime);

            yield return (services, serviceKey, name, lifetime) => serviceKey is null
                ? services.AddChromaCollection<string, Record>(
                    name, sp => CreateClient(ConnectionStringProvider(sp)), lifetime: lifetime)
                : services.AddKeyedChromaCollection<string, Record>(
                    serviceKey, name, sp => CreateClient(ConnectionStringProvider(sp, serviceKey)), lifetime: lifetime);
        }
    }

    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates
    {
        get
        {
            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddChromaVectorStore(ConnectionString, lifetime: lifetime)
                : services.AddKeyedChromaVectorStore(serviceKey, ConnectionString, lifetime: lifetime);

            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services
                    .AddSingleton(sp => CreateClient(ConnectionString))
                    .AddChromaVectorStore(lifetime: lifetime)
                : services
                    .AddSingleton(sp => CreateClient(ConnectionString))
                    .AddKeyedChromaVectorStore(serviceKey, lifetime: lifetime);

            yield return (services, serviceKey, lifetime) => serviceKey is null
                ? services.AddChromaVectorStore(
                    sp => CreateClient(ConnectionStringProvider(sp)), lifetime: lifetime)
                : services.AddKeyedChromaVectorStore(
                    serviceKey, sp => CreateClient(ConnectionStringProvider(sp, serviceKey)), lifetime: lifetime);
        }
    }

    [Fact]
    public void ConnectionStringCantBeNullOrEmpty()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddChromaVectorStore(connectionString: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedChromaVectorStore(serviceKey: "notNull", connectionString: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddChromaCollection<string, Record>(
            name: "notNull", connectionString: null!));
        Assert.Throws<ArgumentException>(() => services.AddChromaCollection<string, Record>(
            name: "notNull", connectionString: ""));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedChromaCollection<string, Record>(
            serviceKey: "notNull", name: "notNull", connectionString: null!));
        Assert.Throws<ArgumentException>(() => services.AddKeyedChromaCollection<string, Record>(
            serviceKey: "notNull", name: "notNull", connectionString: ""));
    }
}
