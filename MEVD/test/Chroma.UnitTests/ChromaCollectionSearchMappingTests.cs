// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using CommunityToolkit.VectorData.Chroma;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace Chroma.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChromaCollectionSearchMapping"/> class.
/// </summary>
public class ChromaCollectionSearchMappingTests
{
    [Theory]
    [InlineData(null, 0.25f, 0.75)]
    [InlineData(DistanceFunction.CosineSimilarity, 0.25f, 0.75)]
    [InlineData(DistanceFunction.CosineDistance, 0.25f, 0.25)]
    [InlineData(DistanceFunction.DotProductSimilarity, 0.25f, 0.75)]
    [InlineData(DistanceFunction.NegativeDotProductSimilarity, 0.25f, -0.75)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, 4f, 4)]
    [InlineData(DistanceFunction.EuclideanDistance, 4f, 2)]
    public void ToScoreConvertsTheChromaDistance(string? distanceFunction, float distance, double expectedScore)
        => Assert.Equal(expectedScore, ChromaCollectionSearchMapping.ToScore(distance, distanceFunction), precision: 6);

    [Theory]
    [InlineData(DistanceFunction.CosineSimilarity, 0.8, 0.7, true)]
    [InlineData(DistanceFunction.CosineSimilarity, 0.6, 0.7, false)]
    [InlineData(DistanceFunction.CosineDistance, 0.2, 0.3, true)]
    [InlineData(DistanceFunction.CosineDistance, 0.4, 0.3, false)]
    [InlineData(DistanceFunction.EuclideanDistance, 1.5, 2, true)]
    [InlineData(DistanceFunction.EuclideanDistance, 2.5, 2, false)]
    [InlineData(null, 0.8, 0.7, true)]
    [InlineData(DistanceFunction.DotProductSimilarity, 0.8, 0.7, true)]
    [InlineData(DistanceFunction.DotProductSimilarity, 0.6, 0.7, false)]
    [InlineData(DistanceFunction.NegativeDotProductSimilarity, -0.8, -0.7, true)]
    [InlineData(DistanceFunction.NegativeDotProductSimilarity, -0.6, -0.7, false)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, 2, 4, true)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, 5, 4, false)]
    public void PassesThresholdKeepsTheSimilarAndTheNear(string? distanceFunction, double score, double threshold, bool expected)
        => Assert.Equal(expected, ChromaCollectionSearchMapping.PassesThreshold(score, threshold, distanceFunction));

    [Fact]
    public void ToScoreThrowsForADistanceFunctionTheModelBuilderRejects()
        => Assert.Throws<UnreachableException>(() => ChromaCollectionSearchMapping.ToScore(0.25f, DistanceFunction.ManhattanDistance));

    [Fact]
    public void PassesThresholdWithoutThresholdKeepsEverything()
        => Assert.True(ChromaCollectionSearchMapping.PassesThreshold(-100, null, DistanceFunction.CosineSimilarity));
}
