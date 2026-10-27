// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.VectorData;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusArrayTests
{
    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    public void SchemaSupportsNativeArrayElements(Type element)
    {
        var definition = new VectorStoreCollectionDefinition { Properties =
            [new VectorStoreKeyProperty("Id", typeof(string)), new VectorStoreDataProperty("Values", element.MakeArrayType()),
             new VectorStoreVectorProperty("Vector", typeof(float[]), 3)] };
        var model = new MilvusModelBuilder().BuildDynamic(definition, null);
        var field = Assert.Single(MilvusCollectionCreateMapping.MapCollectionDefinition(model), f => f.Name == "Values");
        Assert.Equal(MilvusDataType.Array, field.DataType);
        Assert.Equal(MilvusFieldMapping.GetDataType(element), field.ElementDataType);
        Assert.True(field.Nullable);
        Assert.Equal(4096, field.MaxCapacity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapperReportsRecordPropertyAndIndexForNullArrayElements(bool list)
    {
        var model = new MilvusModelBuilder().BuildDynamic(new() { Properties =
            [new VectorStoreKeyProperty("Id", typeof(string)),
             new VectorStoreDataProperty("DisplayTags", list ? typeof(List<string>) : typeof(string[])) { StorageName = "stored_tags" },
             new VectorStoreVectorProperty("Vector", typeof(float[]), 3)] }, null);
        var mapper = new MilvusMapper<Dictionary<string, object?>>(model);
        var record = new Dictionary<string, object?>
        {
            ["Id"] = "record-42",
            ["DisplayTags"] = list ? new List<string> { "a", null!, "b" } : new[] { "a", null!, "b" },
            ["Vector"] = new float[] { 1, 0, 0 },
        };
        var exception = Assert.Throws<ArgumentException>(() => mapper.MapFromDataToStorageModel(record, 3, null));
        Assert.Equal("record", exception.ParamName);
        Assert.Contains("record-42", exception.Message);
        Assert.Contains("batch index 3", exception.Message);
        Assert.Contains("index 1", exception.Message);
        Assert.Contains("DisplayTags", exception.Message);
        Assert.Contains("stored_tags", exception.Message);
    }

    [Fact]
    public void MapperAcceptsNullAndEmptyArrayFields()
    {
        var model = new MilvusModelBuilder().BuildDynamic(new() { Properties =
            [new VectorStoreKeyProperty("Id", typeof(string)), new VectorStoreDataProperty("Tags", typeof(string[])),
             new VectorStoreVectorProperty("Vector", typeof(float[]), 3)] }, null);
        var mapper = new MilvusMapper<Dictionary<string, object?>>(model);
        var record = new Dictionary<string, object?> { ["Id"] = "one", ["Tags"] = null, ["Vector"] = new float[] { 1, 0, 0 } };
        Assert.Null(mapper.MapFromDataToStorageModel(record, 0, null)["Tags"]);
        record["Tags"] = Array.Empty<string>();
        Assert.Empty(Assert.IsType<string[]>(mapper.MapFromDataToStorageModel(record, 0, null)["Tags"]));
    }

    [Fact]
    public void MapperPreservesArraysListsNullsAndEmptyArrays()
    {
        var model = new MilvusModelBuilder().BuildDynamic(new() { Properties =
            [new VectorStoreKeyProperty("Id", typeof(string)), new VectorStoreDataProperty("Array", typeof(string[])),
             new VectorStoreDataProperty("List", typeof(List<int>)), new VectorStoreVectorProperty("Vector", typeof(float[]), 3)] }, null);
        var mapper = new MilvusMapper<Dictionary<string, object?>>(model);
        var mapped = mapper.MapFromStorageToDataModel(new Dictionary<string, object?>
            { ["Id"] = "one", ["Array"] = new List<string> { "a", "b" }, ["List"] = new int[] { 1, 2 } }, false);
        Assert.Equal(new[] { "a", "b" }, Assert.IsType<string[]>(mapped["Array"]));
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<List<int>>(mapped["List"]));
        mapped = mapper.MapFromStorageToDataModel(new Dictionary<string, object?>
            { ["Id"] = "one", ["Array"] = Array.Empty<string>(), ["List"] = null }, false);
        Assert.Empty(Assert.IsType<string[]>(mapped["Array"]));
        Assert.Null(mapped["List"]);
    }
}
