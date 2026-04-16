// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.PgVector;

internal static class PostgresConstants
{
    /// <summary>The name of this vector store for telemetry purposes.</summary>
    public const string VectorStoreSystemName = "postgresql";

    /// <summary>The default schema name.</summary>
    public const string DefaultSchema = "public";

    /// <summary>The name of the column that returns distance value in the database.</summary>
    /// <remarks>It is used in the similarity search query. Must not conflict with model property.</remarks>
    public const string DistanceColumnName = "sk_pg_distance";

    /// <summary>The default index kind.</summary>
    /// <remarks>Defaults to "Flat", which means no indexing.</remarks>
    public const string DefaultIndexKind = IndexKind.Flat;

    /// <summary>The default distance function.</summary>
    public const string DefaultDistanceFunction = DistanceFunction.CosineDistance;

    /// <summary>The default full-text search language for PostgreSQL.</summary>
    public const string DefaultFullTextSearchLanguage = "english";

    public static readonly Dictionary<string, int> IndexMaxDimensions = new()
    {
        { IndexKind.Hnsw, 2000 },
    };
}
