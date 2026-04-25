// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.SqliteVec;

/// <summary>
/// Representation of SQLite column.
/// </summary>
internal sealed class SqliteColumn(
    string name,
    string type,
    bool isPrimary)
{
    public string Name { get; set; } = name;

    public string Type { get; set; } = type;

    public bool IsPrimary { get; set; } = isPrimary;

    public bool IsNullable { get; set; }

    public bool HasIndex { get; set; }

    public Dictionary<string, object>? Configuration { get; set; }
}
