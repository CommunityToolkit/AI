// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// Contains mapping helpers to use when searching for records using Chroma.
/// </summary>
internal static class ChromaCollectionSearchMapping
{
    /// <summary>
    /// Convert the distance Chroma returns to the score of the given distance function.
    /// </summary>
    /// <remarks>
    /// Chroma returns 1 - cosine similarity for cosine, 1 - dot product for ip, and the squared Euclidean distance for l2.
    /// </remarks>
    /// <param name="distance">The distance returned by Chroma.</param>
    /// <param name="distanceFunction">The distance function of the vector property.</param>
    /// <returns>The score.</returns>
    public static double ToScore(float distance, string? distanceFunction)
        => distanceFunction switch
        {
            DistanceFunction.CosineSimilarity or null => 1 - distance,
            DistanceFunction.CosineDistance => distance,
            DistanceFunction.DotProductSimilarity => 1 - distance,
            DistanceFunction.NegativeDotProductSimilarity => distance - 1,
            DistanceFunction.EuclideanSquaredDistance => distance,
            DistanceFunction.EuclideanDistance => Math.Sqrt(distance),

            // The model builder rejects the other distance functions.
            _ => throw new UnreachableException($"Distance function '{distanceFunction}' is not supported by the Chroma VectorStore.")
        };

    /// <summary>
    /// Whether the score passes the threshold: a similarity must reach it, a distance must not go beyond it.
    /// </summary>
    /// <param name="score">The score.</param>
    /// <param name="threshold">The threshold, or <see langword="null"/> for none.</param>
    /// <param name="distanceFunction">The distance function of the vector property.</param>
    public static bool PassesThreshold(double score, double? threshold, string? distanceFunction)
        => threshold is not { } t
            || (distanceFunction is DistanceFunction.CosineSimilarity or DistanceFunction.DotProductSimilarity or null
                ? score >= t
                : score <= t);
}
