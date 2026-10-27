// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData;
using MongoDB.Bson;


namespace CommunityToolkit.VectorData.AzureDocumentDB;

/// <summary>
/// Contains mapping helpers to use when searching for documents using Azure DocumentDB.
/// </summary>
internal static class DocumentDBCollectionSearchMapping
{
    /// <summary>Returns index kind specified on vector property or default <see cref="MongoConstants.DefaultIndexKind"/>.</summary>
    public static string GetVectorPropertyIndexKind(string? indexKind) => !string.IsNullOrWhiteSpace(indexKind) ? indexKind! : MongoConstants.DefaultIndexKind;

    /// <summary>Returns distance function specified on vector property or default <see cref="MongoConstants.DefaultDistanceFunction"/>.</summary>
    public static string GetVectorPropertyDistanceFunction(string? distanceFunction) => !string.IsNullOrWhiteSpace(distanceFunction) ? distanceFunction! : MongoConstants.DefaultDistanceFunction;

    /// <summary>Returns search part of the search query for <see cref="IndexKind.Hnsw"/> index kind.</summary>
    public static BsonDocument GetSearchQueryForHnswIndex<TVector>(
        TVector vector,
        string vectorPropertyName,
        int limit,
        int efSearch,
        BsonDocument? filter)
    {
        var searchQuery = new BsonDocument
        {
            { "vector", BsonArray.Create(vector) },
            { "path", vectorPropertyName },
            { "k", limit },
            { "efSearch", efSearch }
        };

        if (filter is not null)
        {
            searchQuery["filter"] = filter;
        }

        return new BsonDocument
        {
            { "$search",
                new BsonDocument
                {
                    { "cosmosSearch", searchQuery }
                }
            }
        };
    }

    /// <summary>Returns search part of the search query for <see cref="IndexKind.IvfFlat"/> index kind.</summary>
    public static BsonDocument GetSearchQueryForIvfIndex<TVector>(
        TVector vector,
        string vectorPropertyName,
        int limit,
        BsonDocument? filter)
    {
        var searchQuery = new BsonDocument
        {
            { "vector", BsonArray.Create(vector) },
            { "path", vectorPropertyName },
            { "k", limit },
        };

        if (filter is not null)
        {
            searchQuery["filter"] = filter;
        }

        return new BsonDocument
        {
            { "$search",
                new BsonDocument
                {
                    { "cosmosSearch", searchQuery },
                    { "returnStoredSource", true }
                }
            }
        };
    }

    /// <summary>Returns projection part of the search query to return similarity score together with document.</summary>
    public static BsonDocument GetProjectionQuery(string scorePropertyName, string documentPropertyName)
    {
        return new BsonDocument
        {
            { "$project",
                new BsonDocument
                {
                    { scorePropertyName, new BsonDocument { { "$meta", "searchScore" } } },
                    { documentPropertyName, "$$ROOT" }
                }
            }
        };
    }

    /// <summary>Returns the score of a result from the search score that Azure DocumentDB returns.</summary>
    /// <remarks>
    /// For COS Azure DocumentDB returns the cosine similarity: CosineDistance returns 1 - similarity. IP and L2 return the dot
    /// product and the Euclidean distance as they are.
    /// </remarks>
    public static double GetScore(double searchScore, string? distanceFunction)
        => GetVectorPropertyDistanceFunction(distanceFunction) == DistanceFunction.CosineDistance ? 1 - searchScore : searchScore;

    /// <summary>Returns a $match stage to filter results by score threshold.</summary>
    /// <remarks>
    /// The stage filters the search score that Azure DocumentDB returns. For IP higher values mean more similar, so we filter
    /// with $gte; for CosineDistance a distance of at most the threshold is a similarity of at least 1 - threshold.
    /// For L2 it returns a distance where lower values mean more similar, so we filter with $lte.
    /// </remarks>
    public static BsonDocument GetScoreThresholdMatchQuery(string scorePropertyName, double scoreThreshold, string? distanceFunction)
    {
        var (comparisonOperator, searchScore) = GetVectorPropertyDistanceFunction(distanceFunction) switch
        {
            DistanceFunction.CosineDistance => ("$gte", 1 - scoreThreshold),
            DistanceFunction.DotProductSimilarity => ("$gte", scoreThreshold),
            DistanceFunction.EuclideanDistance => ("$lte", scoreThreshold),
            _ => throw new NotSupportedException($"Score threshold is not supported for distance function '{distanceFunction}'.")
        };

        return new()
        {
            {
                "$match", new BsonDocument
                {
                    { scorePropertyName, new BsonDocument { { comparisonOperator, searchScore } } }
                }
            }
        };
    }
}
