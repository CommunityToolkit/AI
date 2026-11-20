// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Milvus.Client.V2.Types;

namespace CommunityToolkit.VectorData.Milvus;

internal static class MilvusCollectionCreateMapping
{
    public static IReadOnlyList<FieldSchema> MapCollectionDefinition(CollectionModel model)
    {
        var key = model.KeyProperty;
        List<FieldSchema> fields = [key.Type == typeof(long)
            ? new FieldSchema(key.StorageName, DataType.Int64, isPrimaryKey: true)
            : FieldSchema.CreateVarchar(key.StorageName, MilvusConstants.MaxVarCharLength, isPrimaryKey: true)];
        foreach (var property in model.DataProperties)
        {
            var type = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
            var nullable = !property.Type.IsValueType || Nullable.GetUnderlyingType(property.Type) is not null;
            if (MilvusFieldMapping.GetElementType(type) is { } element)
            {
                var field = new FieldSchema(property.StorageName, DataType.Array)
                {
                    ElementDataType = MilvusFieldMapping.GetDataType(element),
                    MaxCapacity = MilvusConstants.DefaultArrayCapacity,
                    Nullable = true,
                };
                if (element == typeof(string))
                {
                    field.MaxLength = MilvusConstants.MaxVarCharLength;
                }
                fields.Add(field);
                continue;
            }
            fields.Add(type == typeof(string)
                ? FieldSchema.CreateVarchar(property.StorageName, MilvusConstants.MaxVarCharLength, nullable: true, enableAnalyzer: property.IsFullTextIndexed)
                : new FieldSchema(property.StorageName, MilvusFieldMapping.GetDataType(type)) { Nullable = nullable });
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

    /// <summary>
    /// Maps the model to the indexes of the collection: one per vector property, and a sparse inverted index on the
    /// hidden BM25 field of each full-text indexed property. Each index is named after its field: Milvus.Client.V2 3.0.0
    /// gives every unnamed index the same default name, and Milvus rejects a second index with that name.
    /// </summary>
    public static IReadOnlyList<IndexParam> MapIndexes(CollectionModel model, int hnswM, int hnswEfConstruction)
    {
        List<IndexParam> indexes = [];
        foreach (var vector in model.VectorProperties)
        {
            var indexType = GetIndexType(vector);
            var index = new IndexParam(vector.StorageName, indexName: vector.StorageName, indexType: indexType, metricType: GetMetric(vector));
            if (indexType == IndexType.Hnsw)
            {
                index.ExtraParams["M"] = hnswM.ToString(CultureInfo.InvariantCulture);
                index.ExtraParams["efConstruction"] = hnswEfConstruction.ToString(CultureInfo.InvariantCulture);
            }
            indexes.Add(index);
        }
        foreach (var property in GetFullTextProperties(model))
        {
            indexes.Add(new IndexParam(GetBm25FieldName(property), indexName: GetBm25FieldName(property), indexType: IndexType.SparseInvertedIndex, metricType: MetricType.Bm25));
        }
        return indexes;
    }

    public static IEnumerable<DataPropertyModel> GetFullTextProperties(CollectionModel model)
        => model.DataProperties.Where(p => p.IsFullTextIndexed);

    /// <summary>
    /// Gets the name of the hidden sparse field that holds the BM25 vectors of a full-text indexed property, and of
    /// the BM25 function that fills it: the storage name of the property with <c>_bm25</c>.
    /// </summary>
    public static string GetBm25FieldName(DataPropertyModel property) => property.StorageName + "_bm25";

    public static MetricType GetMetric(VectorPropertyModel property) => property.DistanceFunction switch
    {
        null or DistanceFunction.CosineSimilarity => MetricType.Cosine,
        DistanceFunction.DotProductSimilarity => MetricType.Ip,
        DistanceFunction.EuclideanSquaredDistance or DistanceFunction.EuclideanDistance => MetricType.L2,
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
