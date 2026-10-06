// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;

namespace Chroma.ConformanceTests.Support;

#pragma warning disable CA1810 // Initialize all static fields when those fields are declared

internal static class ChromaTestEnvironment
{
    public static readonly string? ConnectionString;

    public static bool IsConnectionStringDefined => !string.IsNullOrEmpty(ConnectionString);

    static ChromaTestEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path: "testsettings.json", optional: true)
            .AddJsonFile(path: "testsettings.development.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<ChromaTestStore>()
            .Build();

        var chromaSection = configuration.GetSection("Chroma");
        ConnectionString = chromaSection["ConnectionString"];
    }
}
