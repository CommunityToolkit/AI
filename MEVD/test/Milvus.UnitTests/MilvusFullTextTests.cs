// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.VectorData.UnitTests;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Milvus.Client;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusFullTextTests
{
    private static CollectionModel Build(params VectorStoreProperty[] properties)
        => new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties = [new VectorStoreKeyProperty("Id", typeof(string)), .. properties, new VectorStoreVectorProperty("Vector", typeof(float[]), 3)],
        }, null);

    [Fact]
    public void FullTextPropertyGetsAnAnalyzerAHiddenSparseFieldAndABm25Function()
    {
        var schema = MilvusCollectionCreateMapping.MapCollectionSchema(Build(
            new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true, StorageName = "body" },
            new VectorStoreDataProperty("Title", typeof(string))));

        var text = Assert.Single(schema.Fields, f => f.Name == "body");
        Assert.Equal(MilvusDataType.VarChar, text.DataType);
        Assert.True(text.EnableAnalyzer);
        Assert.True(text.Nullable);
        Assert.False(Assert.Single(schema.Fields, f => f.Name == "Title").EnableAnalyzer);
        Assert.Equal(MilvusDataType.SparseFloatVector, Assert.Single(schema.Fields, f => f.Name == "body_bm25").DataType);
        var function = Assert.Single(schema.Functions);
        Assert.Equal(FunctionType.Bm25, function.Type);
        Assert.Equal("body_bm25", function.Name);
        Assert.Equal(["body"], function.InputFieldNames);
        Assert.Equal(["body_bm25"], function.OutputFieldNames);
    }

    [Fact]
    public void EachFullTextPropertyGetsItsOwnBm25Field()
    {
        var schema = MilvusCollectionCreateMapping.MapCollectionSchema(Build(
            new VectorStoreDataProperty("Text1", typeof(string)) { IsFullTextIndexed = true },
            new VectorStoreDataProperty("Text2", typeof(string)) { IsFullTextIndexed = true }));

        Assert.Equal(["Text1_bm25", "Text2_bm25"], schema.Fields.Where(f => f.DataType == MilvusDataType.SparseFloatVector).Select(f => f.Name));
        Assert.Equal(["Text1", "Text2"], schema.Functions.SelectMany(f => f.InputFieldNames));
    }

    [Fact]
    public void ModelWithoutFullTextHasNoBm25FieldOrFunction()
    {
        var schema = MilvusCollectionCreateMapping.MapCollectionSchema(Build(new VectorStoreDataProperty("Text", typeof(string)) { IsIndexed = true }));

        Assert.DoesNotContain(schema.Fields, f => f.DataType == MilvusDataType.SparseFloatVector || f.EnableAnalyzer);
        Assert.Empty(schema.Functions);
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(bool?))]
    [InlineData(typeof(string[]))]
    [InlineData(typeof(List<string>))]
    public void RejectsFullTextOnPropertiesThatAreNotStrings(Type type)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(new VectorStoreDataProperty("Text", type) { IsFullTextIndexed = true }));
        Assert.Contains("'Text'", exception.Message);
        Assert.Contains("string", exception.Message);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("vector")]
    [InlineData("fulltext")]
    public void RejectsAPropertyStoredUnderTheNameOfAHiddenBm25Field(string kind)
    {
        VectorStoreProperty conflict = kind switch
        {
            "data" => new VectorStoreDataProperty("Other", typeof(string)) { StorageName = "Text_bm25" },
            "vector" => new VectorStoreVectorProperty("Other", typeof(float[]), 3) { StorageName = "Text_bm25" },
            _ => new VectorStoreDataProperty("Other", typeof(string)) { StorageName = "Text_bm25", IsFullTextIndexed = true },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => Build(new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true }, conflict));
        Assert.Contains("'Other'", exception.Message);
        Assert.Contains("'Text_bm25'", exception.Message);
        Assert.Contains("'Text'", exception.Message);
    }

    [Fact]
    public void RejectsAKeyStoredUnderTheNameOfAHiddenBm25Field()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties =
            [
                new VectorStoreKeyProperty("Id", typeof(string)) { StorageName = "Text_bm25" },
                new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreVectorProperty("Vector", typeof(float[]), 3),
            ],
        }, null));
        Assert.Contains("'Text_bm25'", exception.Message);
    }

    [Fact]
    public void RejectsFullTextStorageNamesTooLongForTheBm25Field()
    {
        // Milvus field names allow up to 255 characters, and the BM25 field adds "_bm25" to the storage name.
        Build(new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true, StorageName = new string('t', 250) });
        var exception = Assert.Throws<NotSupportedException>(() => Build(new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true, StorageName = new string('t', 251) }));
        Assert.Contains("255", exception.Message);
    }

    [Fact]
    public void SupportsTheDefinitionOfTheChatHistoryMemoryProviderOfAgentFramework()
    {
        // The definition ChatHistoryMemoryProvider of Microsoft.Agents.AI 1.23.0 creates, as a dynamic collection.
        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("Key", typeof(Guid)),
                new VectorStoreDataProperty("Role", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("MessageId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("AuthorName", typeof(string)),
                new VectorStoreDataProperty("ApplicationId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("AgentId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("UserId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("SessionId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("Content", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty("CreatedAt", typeof(string)) { IsIndexed = true },
                new VectorStoreVectorProperty("ContentEmbedding", typeof(string), 3),
            ],
        };
        using var client = new MilvusClient("localhost", port: 1);
        using var store = new MilvusVectorStore(client, ownsClient: false, new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });

        using var collection = store.GetDynamicCollection("chat_history", definition);

        var schema = MilvusCollectionCreateMapping.MapCollectionSchema(new MilvusModelBuilder().BuildDynamic(definition, new FakeEmbeddingGenerator()));
        Assert.True(Assert.Single(schema.Fields, f => f.Name == "Content").EnableAnalyzer);
        Assert.Equal("Content", Assert.Single(Assert.Single(schema.Functions).InputFieldNames));
    }

    [Fact]
    public void SupportsTheDefinitionOfTheTextSearchStoreOfSemanticKernel()
    {
        // TextSearchStore<string> of Microsoft.SemanticKernel.Core 1.80.1 creates a typed collection with this definition.
        using var client = new MilvusClient("localhost", port: 1);
        using var store = new MilvusVectorStore(client, ownsClient: false, new() { EmbeddingGenerator = new FakeEmbeddingGenerator() });

        using var collection = store.GetCollection<string, TextSearchStoreDocument>("docs", TextSearchStoreDocument.Definition(3));

        // TextSearchStore searches with hybrid search when the collection offers it.
        Assert.Same(collection, collection.GetService(typeof(IKeywordHybridSearchable<TextSearchStoreDocument>)));
    }

    internal sealed class TextSearchStoreDocument
    {
        public string Key { get; set; } = string.Empty;

        public List<string> Namespaces { get; set; } = [];

        public string? Text { get; set; }

        public string? SourceId { get; set; }

        public string? SourceName { get; set; }

        public string? SourceLink { get; set; }

        public string? TextEmbedding { get; set; }

        public static VectorStoreCollectionDefinition Definition(int dimensions) => new()
        {
            Properties =
            [
                new VectorStoreKeyProperty("Key", typeof(string)),
                new VectorStoreDataProperty("Namespaces", typeof(List<string>)) { IsIndexed = true },
                new VectorStoreDataProperty("SourceId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty("SourceName", typeof(string)),
                new VectorStoreDataProperty("SourceLink", typeof(string)),
                new VectorStoreVectorProperty("TextEmbedding", typeof(string), dimensions),
            ],
        };
    }
}
