// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.Client;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Milvus;

internal static class MilvusCollectionCreateMapping
{
    public static IReadOnlyList<FieldSchema> MapCollectionDefinition(CollectionModel model)
    {
        var key = model.KeyProperty;
        List<FieldSchema> fields = [key.Type == typeof(long)
            ? FieldSchema.Create<long>(key.StorageName, isPrimaryKey: true)
            : FieldSchema.CreateVarchar(key.StorageName, MilvusConstants.MaxVarCharLength, isPrimaryKey: true)];
        foreach (var property in model.DataProperties)
        {
            var type = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
            var nullable = !property.Type.IsValueType || Nullable.GetUnderlyingType(property.Type) is not null;
            if (MilvusFieldMapping.GetElementType(type) is { } element)
            {
                var name = property.StorageName;
                var field = element == typeof(string) ? FieldSchema.CreateVarcharArray(name, MilvusConstants.DefaultArrayCapacity, MilvusConstants.MaxVarCharLength, nullable: true)
                    : element == typeof(bool) ? FieldSchema.CreateArray<bool>(name, MilvusConstants.DefaultArrayCapacity, nullable: true)
                    : element == typeof(int) ? FieldSchema.CreateArray<int>(name, MilvusConstants.DefaultArrayCapacity, nullable: true)
                    : element == typeof(long) ? FieldSchema.CreateArray<long>(name, MilvusConstants.DefaultArrayCapacity, nullable: true)
                    : element == typeof(float) ? FieldSchema.CreateArray<float>(name, MilvusConstants.DefaultArrayCapacity, nullable: true)
                    : element == typeof(double) ? FieldSchema.CreateArray<double>(name, MilvusConstants.DefaultArrayCapacity, nullable: true)
                    : throw new NotSupportedException($"Unsupported Milvus array element type '{element}'.");
                fields.Add(field);
                continue;
            }
            fields.Add(type == typeof(string)
                ? FieldSchema.CreateVarchar(property.StorageName, MilvusConstants.MaxVarCharLength, nullable: true, enableAnalyzer: property.IsFullTextIndexed)
                : FieldSchema.Create(property.StorageName, MilvusFieldMapping.GetDataType(type), nullable: nullable));
        }
        foreach (var vector in model.VectorProperties)
        {
            fields.Add(FieldSchema.CreateFloatVector(vector.StorageName, vector.Dimensions));
        }
        foreach (var property in GetFullTextProperties(model))
        {
            fields.Add(FieldSchema.CreateSparseFloatVector(GetBm25FieldName(property)));
        }
        return fields;
    }

    /// <summary>
    /// Maps the model to the schema of the collection: its fields, and for each full-text indexed property, a BM25
    /// function that Milvus runs on the text to fill the hidden BM25 field.
    /// </summary>
    public static CollectionSchema MapCollectionSchema(CollectionModel model)
    {
        var schema = new CollectionSchema();
        foreach (var field in MapCollectionDefinition(model))
        {
            schema.Fields.Add(field);
        }
        foreach (var property in GetFullTextProperties(model))
        {
            var bm25FieldName = GetBm25FieldName(property);
            schema.Functions.Add(FunctionSchema.CreateBm25(bm25FieldName, property.StorageName, bm25FieldName));
        }
        return schema;
    }

    public static IEnumerable<DataPropertyModel> GetFullTextProperties(CollectionModel model)
        => model.DataProperties.Where(p => p.IsFullTextIndexed);

    /// <summary>
    /// Gets the name of the hidden sparse field that holds the BM25 vectors of a full-text indexed property, and of
    /// the BM25 function that fills it: the storage name of the property with <c>_bm25</c>.
    /// </summary>
    public static string GetBm25FieldName(DataPropertyModel property) => property.StorageName + "_bm25";

    public static SimilarityMetricType GetMetric(VectorPropertyModel property) => property.DistanceFunction switch
    {
        null or DistanceFunction.CosineSimilarity => SimilarityMetricType.Cosine,
        DistanceFunction.DotProductSimilarity => SimilarityMetricType.Ip,
        DistanceFunction.EuclideanSquaredDistance or DistanceFunction.EuclideanDistance => SimilarityMetricType.L2,
        _ => throw new NotSupportedException($"Milvus does not support distance function '{property.DistanceFunction}'."),
    };

    public static IndexType GetIndexType(VectorPropertyModel property) => property.IndexKind switch
    {
        null => IndexType.AutoIndex,
        IndexKind.Flat => IndexType.Flat,
        IndexKind.Hnsw => IndexType.Hnsw,
        _ => throw new NotSupportedException($"Milvus does not support index kind '{property.IndexKind}'."),
    };
}
