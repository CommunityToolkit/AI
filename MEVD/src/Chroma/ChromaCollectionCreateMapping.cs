// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// Contains mapping helpers to use when creating a Chroma collection.
/// </summary>
internal static class ChromaCollectionCreateMapping
{
    /// <summary>The distance functions Chroma supports, with its three spaces.</summary>
    private const string SupportedDistanceFunctions =
        $"{DistanceFunction.CosineSimilarity}, {DistanceFunction.CosineDistance}, {DistanceFunction.DotProductSimilarity}, " +
        $"{DistanceFunction.NegativeDotProductSimilarity}, {DistanceFunction.EuclideanSquaredDistance}, {DistanceFunction.EuclideanDistance}";

    /// <summary>
    /// Maps the vector property and the BM25 indexes to the definition of the Chroma collection.
    /// </summary>
    /// <param name="name">The name of the collection.</param>
    /// <param name="vectorProperty">The vector property.</param>
    /// <param name="bm25Properties">The properties to create a BM25 index for.</param>
    /// <param name="documentProperty">The property stored as the document, whose BM25 index comes from the document, or <see langword="null"/>.</param>
    /// <returns>The definition to create the collection with.</returns>
    public static ChromaCollectionDefinition MapCollectionDefinition(string name, VectorPropertyModel vectorProperty, IReadOnlyList<DataPropertyModel> bm25Properties, DataPropertyModel? documentProperty = null)
    {
        ChromaCollectionSchema? schema = null;
        foreach (var property in bm25Properties)
        {
            schema = (schema ?? new()).WithBm25Index(property == documentProperty ? ChromaSearchKeys.Document : property.StorageName, ifSupported: true);
        }

        return new(name) { Configuration = new() { Space = GetSpace(vectorProperty) }, Schema = schema };
    }

    /// <summary>
    /// Get the string properties with full-text indexing, the ones a BM25 index can be created for.
    /// </summary>
    /// <param name="model">The model of the collection.</param>
    public static List<DataPropertyModel> GetBm25Properties(CollectionModel model)
        => model.DataProperties.Where(ChromaFieldMapping.IsFullTextString).ToList();

    /// <summary>
    /// Get the metadata key of the BM25 index of a property, as the client names it: <c>document_bm25</c> for the property
    /// stored as the document, else the storage name with <c>_bm25</c>.
    /// </summary>
    /// <param name="property">The full-text property.</param>
    /// <param name="documentProperty">The property stored as the document, or <see langword="null"/>.</param>
    public static string GetBm25IndexKey(DataPropertyModel property, DataPropertyModel? documentProperty)
        => property == documentProperty ? "document_bm25" : property.StorageName + "_bm25";

    /// <summary>
    /// Get a value indicating whether Chroma Cloud takes a metadata key. The client creates no BM25 index whose key is longer
    /// than that.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    public static bool FitsChromaCloud(string key)
        => Encoding.UTF8.GetByteCount(key) <= ChromaCloudQuotas.MaxMetadataKeyBytes;

    /// <summary>
    /// Get the Chroma distance function, called space, for the given <paramref name="vectorProperty"/>.
    /// If none is configured, the default is cosine.
    /// </summary>
    /// <param name="vectorProperty">The vector property definition.</param>
    /// <returns>The Chroma space.</returns>
    /// <exception cref="NotSupportedException">Thrown if a distance function is chosen that Chroma does not support.</exception>
    public static ChromaSpace GetSpace(VectorPropertyModel vectorProperty)
        => vectorProperty.DistanceFunction switch
        {
            DistanceFunction.CosineSimilarity or DistanceFunction.CosineDistance or null => ChromaSpace.Cosine,
            DistanceFunction.DotProductSimilarity or DistanceFunction.NegativeDotProductSimilarity => ChromaSpace.InnerProduct,
            DistanceFunction.EuclideanSquaredDistance or DistanceFunction.EuclideanDistance => ChromaSpace.L2,

            _ => throw new NotSupportedException(
                $"Distance function '{vectorProperty.DistanceFunction}' for {nameof(VectorStoreVectorProperty)} '{vectorProperty.ModelName}' is not supported by the Chroma VectorStore. " +
                $"Supported distance functions: {SupportedDistanceFunctions}.")
        };
}
