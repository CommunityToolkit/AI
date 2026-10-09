// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text.Json.Nodes;
using ChromaDB.Client;
using CommunityToolkit.VectorData.Chroma;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Xunit;

namespace Chroma.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChromaCollectionCreateMapping"/> class.
/// </summary>
public class ChromaCollectionCreateMappingTests
{
    [Theory]
    [InlineData(null, ChromaSpace.Cosine)]
    [InlineData(DistanceFunction.CosineSimilarity, ChromaSpace.Cosine)]
    [InlineData(DistanceFunction.CosineDistance, ChromaSpace.Cosine)]
    [InlineData(DistanceFunction.DotProductSimilarity, ChromaSpace.InnerProduct)]
    [InlineData(DistanceFunction.NegativeDotProductSimilarity, ChromaSpace.InnerProduct)]
    [InlineData(DistanceFunction.EuclideanDistance, ChromaSpace.L2)]
    [InlineData(DistanceFunction.EuclideanSquaredDistance, ChromaSpace.L2)]
    public void MapCollectionDefinitionSetsTheSpace(string? distanceFunction, ChromaSpace expectedSpace)
    {
        // Arrange.
        var vectorProperty = new VectorPropertyModel("Vector", typeof(ReadOnlyMemory<float>)) { DistanceFunction = distanceFunction };

        // Act.
        var definition = ChromaCollectionCreateMapping.MapCollectionDefinition("hotels", vectorProperty, []);

        // Assert.
        Assert.Equal("hotels", definition.Name);
        Assert.Equal(expectedSpace, definition.Configuration?.Space);
        Assert.Null(definition.Schema);
    }

    [Theory]
    [InlineData(DistanceFunction.ManhattanDistance)]
    [InlineData(DistanceFunction.HammingDistance)]
    public void MapCollectionDefinitionThrowsForUnsupportedDistanceFunction(string distanceFunction)
    {
        // Arrange.
        var vectorProperty = new VectorPropertyModel("Vector", typeof(ReadOnlyMemory<float>)) { DistanceFunction = distanceFunction };

        // Act and assert.
        Assert.Throws<NotSupportedException>(() => ChromaCollectionCreateMapping.MapCollectionDefinition("hotels", vectorProperty, []));
    }

    [Fact]
    public void MapCollectionDefinitionAddsASchemaForTheBm25Properties()
    {
        // Arrange.
        var model = ChromaTestModel.Build<TwoFullTextHotel>();

        // Act.
        var definition = ChromaCollectionCreateMapping.MapCollectionDefinition("hotels", model.VectorProperty, ChromaCollectionCreateMapping.GetBm25Properties(model));

        // Assert.
        var expected =
            """{"defaults":{},"keys":{"Description_bm25":"""
            + """{"sparse_vector":{"sparse_vector_index":{"enabled":true,"config":{"source_key":"Description","bm25":true,"embedding_function":{"type":"known","name":"chroma_bm25","config":{"k":1.2,"b":0.75,"avg_doc_length":256,"token_max_length":40,"include_tokens":false}}}}}},"Review_bm25":"""
            + """{"sparse_vector":{"sparse_vector_index":{"enabled":true,"config":{"source_key":"Review","bm25":true,"embedding_function":{"type":"known","name":"chroma_bm25","config":{"k":1.2,"b":0.75,"avg_doc_length":256,"token_max_length":40,"include_tokens":false}}}}}}}}""";
        var actual = definition.Schema!.ToString();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), actual);
    }

    [Fact]
    public void MapCollectionDefinitionTakesTheBm25VectorsOfTheDocumentPropertyFromTheDocument()
    {
        // Arrange: the only full-text property is stored as the document.
        var model = ChromaTestModel.Build<FullTextHotel>();

        // Act.
        var definition = ChromaCollectionCreateMapping.MapCollectionDefinition("hotels", model.VectorProperty, ChromaCollectionCreateMapping.GetBm25Properties(model), ChromaFieldMapping.GetDocumentProperty(model));

        // Assert.
        Assert.Contains("\"document_bm25\":{\"sparse_vector\":{\"sparse_vector_index\":{\"enabled\":true,\"config\":{", definition.Schema!.ToString());
        Assert.Contains("\"source_key\":\"#document\"", definition.Schema.ToString());
    }

    [Fact]
    public void GetBm25PropertiesTakesTheStringPropertiesWithFullTextIndexing()
    {
        Assert.Equal(["Description", "Review"], ChromaCollectionCreateMapping.GetBm25Properties(ChromaTestModel.Build<TwoFullTextHotel>()).Select(p => p.ModelName));
        Assert.Empty(ChromaCollectionCreateMapping.GetBm25Properties(ChromaTestModel.Build<ChromaHotel<string>>()));
    }
}
