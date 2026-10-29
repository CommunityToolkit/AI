// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusMappingTests
{
    private static CollectionModel Model() => new MilvusModelBuilder().Build(typeof(MilvusTestRecord), typeof(Guid), null, null);

    [Fact]
    public void SchemaUsesNativeFieldsAndStorageNames()
    {
        var fields = MilvusCollectionCreateMapping.MapCollectionDefinition(Model());
        var key = Assert.Single(fields, f => f.IsPrimaryKey);
        Assert.Equal("record_id", key.Name);
        Assert.Equal(MilvusDataType.VarChar, key.DataType);
        Assert.False(key.AutoId);
        Assert.True(Assert.Single(fields, f => f.Name == "label").Nullable);
        var count = Assert.Single(fields, f => f.Name == "Count");
        Assert.True(count.Nullable);
        Assert.Equal(MilvusDataType.Int32, count.DataType);
        Assert.Equal(3, Assert.Single(fields, f => f.Name == "Vector").Dimension);
    }

    [Fact]
    public void LongKeyUsesInt64()
    {
        var model = new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties = [new VectorStoreKeyProperty("Id", typeof(long)), new VectorStoreVectorProperty("Vector", typeof(float[]), 3)],
        }, null);
        Assert.Equal(MilvusDataType.Int64, MilvusCollectionCreateMapping.MapCollectionDefinition(model)[0].DataType);
    }

    [Fact]
    public void MapperRoundTripsNullsGuidAndVectors()
    {
        var mapper = new MilvusMapper<MilvusTestRecord>(Model());
        var record = new MilvusTestRecord { Key = Guid.NewGuid(), Name = null, Count = null, Enabled = true, Vector = new float[] { 1, 2, 3 } };
        var stored = mapper.MapFromDataToStorageModel(record, 0, null);
        Assert.Equal(record.Key.ToString("D"), stored["record_id"]);
        var result = mapper.MapFromStorageToDataModel(new Dictionary<string, object?>(stored), true);
        Assert.Equal(record.Key, result.Key);
        Assert.Null(result.Name);
        Assert.Null(result.Count);
        Assert.True(result.Enabled);
        Assert.Equal(record.Vector.ToArray(), result.Vector.ToArray());
    }

    [Fact]
    public void MapperExcludesVectorsWhenRequested()
    {
        var mapper = new MilvusMapper<MilvusTestRecord>(Model());
        var result = mapper.MapFromStorageToDataModel(new Dictionary<string, object?>
        {
            ["record_id"] = Guid.NewGuid().ToString("D"), ["Vector"] = new float[] { 1, 2, 3 },
        }, false);
        Assert.True(result.Vector.IsEmpty);
    }

    [Fact]
    public void MapperRejectsWrongDimensions()
    {
        var mapper = new MilvusMapper<MilvusTestRecord>(Model());
        Assert.Throws<ArgumentException>(() => mapper.MapFromDataToStorageModel(new() { Vector = new float[] { 1 } }, 0, null));
    }

    [Theory]
    [InlineData(null, SimilarityMetricType.Cosine)]
    [InlineData(DistanceFunction.CosineSimilarity, SimilarityMetricType.Cosine)]
    [InlineData(DistanceFunction.DotProductSimilarity, SimilarityMetricType.Ip)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, SimilarityMetricType.L2)]
    [InlineData(DistanceFunction.EuclideanDistance, SimilarityMetricType.L2)]
    public void MapsSupportedDistances(string? distance, SimilarityMetricType expected)
        => Assert.Equal(expected, MilvusCollectionCreateMapping.GetMetric(new VectorPropertyModel("Vector", typeof(float[])) { Dimensions = 3, DistanceFunction = distance }));

    [Theory]
    [InlineData(DistanceFunction.ManhattanDistance)]
    [InlineData(DistanceFunction.CosineDistance)]
    public void RejectsUnsupportedDistances(string distance)
        => Assert.Throws<NotSupportedException>(() => MilvusCollectionCreateMapping.GetMetric(new VectorPropertyModel("Vector", typeof(float[])) { Dimensions = 3, DistanceFunction = distance }));

    [Theory]
    [InlineData(null, IndexType.AutoIndex)]
    [InlineData(IndexKind.Flat, IndexType.Flat)]
    [InlineData(IndexKind.Hnsw, IndexType.Hnsw)]
    public void MapsSupportedIndexes(string? kind, IndexType expected)
        => Assert.Equal(expected, MilvusCollectionCreateMapping.GetIndexType(new VectorPropertyModel("Vector", typeof(float[])) { Dimensions = 3, IndexKind = kind }));

    [Theory]
    [InlineData(DistanceFunction.CosineSimilarity, .8, .7, true)]
    [InlineData(DistanceFunction.CosineSimilarity, .6, .7, false)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, .6, .7, true)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, .8, .7, false)]
    [InlineData(DistanceFunction.EuclideanDistance, .6, .7, true)]
    [InlineData(DistanceFunction.EuclideanDistance, .8, .7, false)]
    public void AppliesMetricThresholdDirection(string distance, double score, double threshold, bool expected)
        => Assert.Equal(expected, MilvusCollectionSearchMapping.PassesThreshold(score, threshold, distance));

    [Theory]
    [InlineData(DistanceFunction.EuclideanDistance, 4f, 2d)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, 4f, 4d)]
    [InlineData(DistanceFunction.CosineSimilarity, .5f, .5d)]
    [InlineData(DistanceFunction.DotProductSimilarity, -2f, -2d)]
    [InlineData(null, .5f, .5d)]
    public void MapsMilvusScores(string? distance, float score, double expected)
        => Assert.Equal(expected, MilvusCollectionSearchMapping.MapScore(score, distance));

    [Theory]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(int))]
    public void RejectsUnsupportedKeyTypes(Type keyType)
        => Assert.Throws<NotSupportedException>(() => new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties = [new VectorStoreKeyProperty("Id", keyType), new VectorStoreVectorProperty("Vector", typeof(float[]), 3)],
        }, null));

    [Fact]
    public void RejectsAutoGeneratedStringKeysDuringModelConstruction()
    {
        var exception = Assert.Throws<NotSupportedException>(() => new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties = [new VectorStoreKeyProperty("Id", typeof(string)) { IsAutoGenerated = true },
                new VectorStoreVectorProperty("Vector", typeof(float[]), 3)],
        }, null));
        Assert.Contains("Id", exception.Message);
        Assert.Contains("auto-generation", exception.Message);
    }

    [Theory]
    [InlineData("like")]
    [InlineData("AND")]
    [InlineData("minimum_should_match")]
    [InlineData("MINIMUM_SHOULD_MATCH")]
    [InlineData("Array_Contains")]
    [InlineData("true")]
    [InlineData("bad-name")]
    [InlineData("0name")]
    [InlineData("a] or true")]
    public void RejectsInvalidFieldNames(string name)
        => Assert.Throws<NotSupportedException>(() => new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties = [new VectorStoreKeyProperty("Id", typeof(string)) { StorageName = name }, new VectorStoreVectorProperty("Vector", typeof(float[]), 3)],
        }, null));

    [Fact]
    public void SupportsMultipleNamedVectorsAndGeneratedEmbeddings()
    {
        var model = new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties = [new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreVectorProperty("First", typeof(float[]), 3), new VectorStoreVectorProperty("Second", typeof(Embedding<float>), 3)],
        }, null);
        var mapper = new MilvusMapper<Dictionary<string, object?>>(model);
        var row = mapper.MapFromDataToStorageModel(new()
        {
            ["Id"] = "one", ["First"] = new float[] { 1, 2, 3 }, ["Second"] = new Embedding<float>(new float[] { 3, 2, 1 }),
        }, 0, null);
        var result = mapper.MapFromStorageToDataModel(new Dictionary<string, object?>(row), true);
        Assert.Equal(new float[] { 1, 2, 3 }, Assert.IsType<float[]>(result["First"]));
        Assert.Equal(new float[] { 3, 2, 1 }, Assert.IsType<Embedding<float>>(result["Second"]).Vector.ToArray());
    }
}
