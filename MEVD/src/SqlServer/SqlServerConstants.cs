// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.SqlServer;

internal static class SqlServerConstants
{
    internal const string VectorStoreSystemName = "microsoft.sql_server";

    // The actual number is actually higher (2_100), but we want to avoid any kind of "off by one" errors.
    internal const int MaxParameterCount = 2_000;

    internal const int MaxIndexNameLength = 128;
}
