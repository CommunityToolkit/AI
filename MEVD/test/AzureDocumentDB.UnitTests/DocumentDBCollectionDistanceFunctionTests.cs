// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using CommunityToolkit.VectorData.AzureDocumentDB;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using MongoDB.Bson;
using Xunit;

namespace AzureDocumentDB.UnitTests;

public sealed class DocumentDBCollectionDistanceFunctionTests
{
    [Theory]
    [InlineData(DistanceFunction.CosineDistance, "COS")]
    [InlineData(DistanceFunction.CosineSimilarity, "COS")]
    [InlineData(DistanceFunction.DotProductSimilarity, "IP")]
    [InlineData(DistanceFunction.EuclideanDistance, "L2")]
    [InlineData(null, "COS")]
    public void IndexUsesTheSimilarityOfTheDistanceFunction(string? distanceFunction, string expectedSimilarity)
    {
        var vectorProperty = new VectorPropertyModel("vector", typeof(ReadOnlyMemory<float>)) { Dimensions = 4, DistanceFunction = distanceFunction };

        var indexes = DocumentDBCollectionCreateMapping.GetVectorIndexes([vectorProperty], new HashSet<string?>(), numLists: 1, efConstruction: 64);

        Assert.Equal(expectedSimilarity, indexes[0]["cosmosSearchOptions"]["similarity"].AsString);
    }

    // COS returns the cosine similarity: CosineDistance gives 1 - similarity, and its threshold keeps the distances up to it.
    [Theory]
    [InlineData(DistanceFunction.CosineDistance, 0.25, 0.75)]
    [InlineData(null, 0.25, 0.75)]
    [InlineData(DistanceFunction.CosineSimilarity, 0.75, 0.75)]
    [InlineData(DistanceFunction.DotProductSimilarity, 0.75, 0.75)]
    [InlineData(DistanceFunction.EuclideanDistance, 0.75, 0.75)]
    public void ScoreFollowsTheDistanceFunction(string? distanceFunction, double expectedScore, double searchScore)
        => Assert.Equal(expectedScore, DocumentDBCollectionSearchMapping.GetScore(searchScore, distanceFunction), precision: 10);

    [Theory]
    [InlineData(DistanceFunction.CosineDistance, 0.25, "$gte", 0.75)]
    [InlineData(null, 0.25, "$gte", 0.75)]
    [InlineData(DistanceFunction.CosineSimilarity, 0.75, "$gte", 0.75)]
    [InlineData(DistanceFunction.DotProductSimilarity, 0.75, "$gte", 0.75)]
    [InlineData(DistanceFunction.EuclideanDistance, 0.75, "$lte", 0.75)]
    public void ScoreThresholdFollowsTheDistanceFunction(string? distanceFunction, double scoreThreshold, string expectedOperator, double expectedSearchScore)
    {
        var match = DocumentDBCollectionSearchMapping.GetScoreThresholdMatchQuery("score", scoreThreshold, distanceFunction);

        var condition = match["$match"]["score"].AsBsonDocument;
        Assert.Equal(expectedOperator, condition.GetElement(0).Name);
        Assert.Equal(expectedSearchScore, condition[0].AsDouble, precision: 10);
    }
}
