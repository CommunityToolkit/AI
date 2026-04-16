// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using InMemory.ConformanceTests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace InMemory.ConformanceTests.ModelTests;

public class InMemoryDynamicModelTests(InMemoryDynamicModelTests.Fixture fixture)
    : DynamicModelTests<int>(fixture), IClassFixture<InMemoryDynamicModelTests.Fixture>
{
    public override async Task GetAsync_single_record(bool includeVectors)
    {
        if (includeVectors)
        {
            await base.GetAsync_single_record(includeVectors);
            return;
        }

        // InMemory always returns the vectors (IncludeVectors = false isn't respected)
        var expectedRecord = fixture.TestData[0];
        var received = await fixture.Collection.GetAsync(
            (int)expectedRecord[KeyPropertyName]!,
            new() { IncludeVectors = false });

        AssertEquivalent(expectedRecord, received, includeVectors: true, fixture.TestStore.VectorsComparable);
    }

    public override async Task GetAsync_multiple_records(bool includeVectors)
    {
        if (includeVectors)
        {
            await base.GetAsync_multiple_records(includeVectors);
            return;
        }

        // InMemory always returns the vectors (IncludeVectors = false isn't respected)
        var expectedRecords = fixture.TestData.Take(2);
        var ids = expectedRecords.Select(record => record[KeyPropertyName]!);

        var received = await fixture.Collection.GetAsync(ids, new() { IncludeVectors = false }).ToArrayAsync();

        foreach (var record in expectedRecords)
        {
            AssertEquivalent(
                record,
                received.Single(r => r[KeyPropertyName]!.Equals(record[KeyPropertyName])),
                includeVectors: true,
                fixture.TestStore.VectorsComparable);
        }
    }

    public override async Task GetAsync_with_filter(bool includeVectors)
    {
        if (includeVectors)
        {
            await base.GetAsync_with_filter(includeVectors);
            return;
        }

        // InMemory always returns the vectors (IncludeVectors = false isn't respected)
        var expectedRecord = fixture.TestData[0];

        var results = await fixture.Collection.GetAsync(
            r => (int)r[IntegerPropertyName]! == 1,
            top: 2,
            new() { IncludeVectors = includeVectors })
            .ToListAsync();

        var receivedRecord = Assert.Single(results);
        AssertEquivalent(expectedRecord, receivedRecord, includeVectors: true, fixture.TestStore.VectorsComparable);
    }

    public override async Task SearchAsync(bool includeVectors)
    {
        if (includeVectors)
        {
            await base.SearchAsync(includeVectors);
            return;
        }

        // InMemory always returns the vectors (IncludeVectors = false isn't respected)
        var expectedRecord = fixture.TestData[0];

        var result = await Collection
            .SearchAsync(
                expectedRecord[VectorPropertyName]!,
                top: 1,
                new() { IncludeVectors = includeVectors })
            .SingleAsync();

        AssertEquivalent(expectedRecord, result.Record, includeVectors: true, fixture.TestStore.VectorsComparable);
    }

    public override async Task SearchAsync_with_Filter()
    {
        // InMemory always returns the vectors (IncludeVectors = false isn't respected)
        var result = await Collection
            .SearchAsync(
                fixture.TestData[0][VectorPropertyName]!,
                top: 1,
                new() { Filter = r => (int)r[IntegerPropertyName]! == 2 })
            .SingleAsync();

        AssertEquivalent(fixture.TestData[1], result.Record, includeVectors: true, fixture.TestStore.VectorsComparable);
    }

    public override async Task SearchAsync_with_Skip()
    {
        // InMemory always returns the vectors (IncludeVectors = false isn't respected)
        var result = await Collection
            .SearchAsync(
                fixture.TestData[0][VectorPropertyName]!,
                top: 1,
                new() { Skip = 1 })
            .SingleAsync();

        AssertEquivalent(fixture.TestData[1], result.Record, includeVectors: true, fixture.TestStore.VectorsComparable);
    }

    public new class Fixture : DynamicModelTests<int>.Fixture
    {
        public override TestStore TestStore => InMemoryTestStore.Instance;
    }
}
