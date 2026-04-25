// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;

namespace CosmosMongoDB.ConformanceTests.Support;

#pragma warning disable CA1810 // Initialize all static fields when those fields are declared

public static class CosmosMongoTestEnvironment
{
    public static readonly string? ConnectionString;

    public static bool IsConnectionStringDefined => ConnectionString is not null;

    static CosmosMongoTestEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path: "testsettings.json", optional: true)
            .AddJsonFile(path: "testsettings.development.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<CosmosMongoTestStore>()
            .Build();

        ConnectionString = configuration["CosmosMongo:ConnectionString"];
    }
}
