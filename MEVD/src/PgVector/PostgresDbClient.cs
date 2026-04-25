// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Npgsql;

namespace CommunityToolkit.VectorData.PgVector;

/// <summary>
/// A reference-counting wrapper around an <see cref="NpgsqlDataSource"/> instance.
/// </summary>
internal sealed class NpgsqlDataSourceArc(NpgsqlDataSource dataSource) : IDisposable
{
    private int _referenceCount = 1;

    public void Dispose()
    {
        if (Interlocked.Decrement(ref _referenceCount) == 0)
        {
            dataSource.Dispose();
        }
    }

    internal NpgsqlDataSourceArc IncrementReferenceCount()
    {
        Interlocked.Increment(ref _referenceCount);

        return this;
    }
}
