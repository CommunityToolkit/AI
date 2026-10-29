// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.Milvus;

internal static class MilvusConstants
{
    internal const string VectorStoreSystemName = "milvus";

    // The two searches of a hybrid search rank at least this many records each. When they rank only the first top + skip,
    // a record found by both can miss one of the rankings, as when records tie in the vector search, and the fusion ranks it lower.
    internal const int HybridSearchCandidates = 10;

    internal const int MaxSearchLimit = 16384;

    internal const int MaxVarCharLength = 65535;

    internal const int DefaultArrayCapacity = 4096;

    internal const int DefaultPort = 19530;

    internal const int DefaultHnswSearchEf = 64;

    internal const int MaxFieldNameLength = 255;

    /// <summary>The name of the upsert operation for telemetry purposes.</summary>
    internal const string UpsertName = "Upsert";

    /// <summary>The name of the Delete operation for telemetry purposes.</summary>
    internal const string DeleteName = "Delete";
}
