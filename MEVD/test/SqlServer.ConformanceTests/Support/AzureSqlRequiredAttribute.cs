// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Data.SqlClient;
using Xunit;

namespace SqlServer.ConformanceTests.Support;

/// <summary>
/// Helpers for tests that require Azure SQL Database or SQL database in Microsoft Fabric.
/// </summary>
internal static class AzureSqlHelper
{
    private static bool? s_isAzureSql;

    public static async Task SkipUnlessAzureSqlAsync()
        => Assert.SkipUnless(
            await GetIsAzureSqlAsync(),
            "This test requires Azure SQL Database or SQL database in Microsoft Fabric.");

    public static async Task<bool> GetIsAzureSqlAsync()
    {
        if (s_isAzureSql is null)
        {
            // Without a connection string the tests run against the SQL Server testcontainer.
            if (!SqlServerTestEnvironment.IsConnectionStringDefined)
            {
                s_isAzureSql = false;
                return false;
            }

            using SqlConnection connection = new(SqlServerTestEnvironment.ConnectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SERVERPROPERTY('EngineEdition')";
            var result = await command.ExecuteScalarAsync();
            var engineEdition = Convert.ToInt32(result);

            // 5 = Azure SQL Database, 11 = SQL database in Microsoft Fabric
            s_isAzureSql = engineEdition is 5 or 11;
        }

        return s_isAzureSql.Value;
    }
}
