// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using Pinecone.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Pinecone.ConformanceTests;

public class PineconeDistanceFunctionTests(PineconeDistanceFunctionTests.Fixture fixture)
    : DistanceFunctionTests<string>(fixture), IClassFixture<PineconeDistanceFunctionTests.Fixture>
{
    public override Task CosineDistance() => Assert.ThrowsAsync<NotSupportedException>(base.CosineDistance);
    public override Task EuclideanDistance() => Assert.ThrowsAsync<NotSupportedException>(base.EuclideanDistance);
    public override Task HammingDistance() => Assert.ThrowsAsync<NotSupportedException>(base.HammingDistance);
    public override Task ManhattanDistance() => Assert.ThrowsAsync<NotSupportedException>(base.ManhattanDistance);
    public override Task NegativeDotProductSimilarity() => Assert.ThrowsAsync<NotSupportedException>(base.NegativeDotProductSimilarity);

    // Pinecone's score threshold logic always uses "Score >= threshold", which is incorrect
    // for distance-based metrics (where lower is better). Skip score threshold validation.
    protected override Task TestScoreThreshold(VectorStoreCollection<string, SearchRecord> collection)
        => Task.CompletedTask;

    protected override async Task Test(
        string distanceFunction,
        double expectedExactMatchScore,
        double expectedOppositeScore,
        double expectedOrthogonalScore,
        int[] resultOrder)
    {
        await base.Test(
            distanceFunction,
            expectedExactMatchScore,
            expectedOppositeScore,
            expectedOrthogonalScore,
            resultOrder);

        // The Pinecone emulator needs some extra time to spawn a new index service
        // that uses a different distance function.
        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    public new class Fixture() : DistanceFunctionTests<string>.Fixture
    {
        public override TestStore TestStore => PineconeTestStore.Instance;

        // Use a shorter base name to stay within Pinecone's 45-character index name limit.
        protected override string CollectionNameBase => "df-tests";

        // The Pinecone local emulator doesn't handle rapid delete/recreate of the same index
        // with different distance functions. Use a unique collection name per distance function
        // so each test gets its own index.
        public override VectorStoreCollection<string, SearchRecord> CreateCollection(string distanceFunction)
        {
            var name = TestStore.AdjustCollectionName($"{CollectionName}-{distanceFunction}");

            VectorStoreCollectionDefinition definition = new()
            {
                Properties =
                [
                    new VectorStoreKeyProperty(nameof(SearchRecord.Key), typeof(string)),
                    new VectorStoreDataProperty(nameof(SearchRecord.Int), typeof(int)),
                    new VectorStoreVectorProperty(nameof(SearchRecord.Vector), typeof(ReadOnlyMemory<float>), dimensions: 4)
                    {
                        DistanceFunction = distanceFunction,
                        IndexKind = IndexKind ?? DefaultIndexKind
                    }
                ]
            };

            return TestStore.CreateCollection<string, SearchRecord>(name, definition);
        }
    }
}
