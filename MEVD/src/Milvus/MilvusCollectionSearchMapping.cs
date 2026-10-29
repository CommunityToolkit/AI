// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.Milvus;

internal static class MilvusCollectionSearchMapping
{
    // For L2, Milvus returns the squared Euclidean distance: the Euclidean distance is its square root.
    public static double MapScore(float score, string? distanceFunction)
        => distanceFunction == DistanceFunction.EuclideanDistance ? Math.Sqrt(score) : score;

    public static bool PassesThreshold(double score, double? threshold, string? distanceFunction)
        => threshold is null || (distanceFunction is DistanceFunction.EuclideanSquaredDistance or DistanceFunction.EuclideanDistance
            ? score <= threshold.Value : score >= threshold.Value);
}
