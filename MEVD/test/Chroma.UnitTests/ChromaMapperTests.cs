// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.VectorData.Chroma;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace Chroma.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChromaMapper{TRecord}"/> class.
/// </summary>
public class ChromaMapperTests
{
    [Fact]
    public void MapsAGuidKeyToItsStringForm()
    {
        // Arrange.
        var key = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sut = new ChromaMapper<ChromaHotel<Guid>>(ChromaTestModel.Build<ChromaHotel<Guid>>());
        var hotel = new ChromaHotel<Guid> { HotelId = key, Embedding = new float[] { 1, 2, 3, 4 } };

        // Act.
        var storageRecord = sut.MapFromDataToStorageModel(hotel, 0, generatedEmbeddings: null);

        // Assert.
        Assert.Equal("11111111-1111-1111-1111-111111111111", storageRecord.Id);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, storageRecord.Embedding.ToArray());
    }

    [Fact]
    public void WritesNullPropertiesAsExplicitNulls()
    {
        // An upsert of an existing record merges its metadata in Chroma: an explicit null deletes the old value.
        // Arrange.
        var sut = new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>());
        var hotel = new ChromaHotel<string> { HotelId = "h1", HotelName = null, Rating = 4, Embedding = new float[] { 1, 2, 3, 4 } };

        // Act.
        var metadata = sut.MapFromDataToStorageModel(hotel, 0, generatedEmbeddings: null).Metadata!;

        // Assert.
        Assert.True(metadata.ContainsKey("HotelName"));
        Assert.Null(metadata["HotelName"]);
        Assert.Equal(4, metadata["Rating"]);
        Assert.Equal(0d, metadata["Price"]);
        Assert.Equal(false, metadata["parking_is_included"]);
    }

    [Fact]
    public void WritesEmptyListsAsExplicitNulls()
    {
        // Arrange.
        var sut = new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>());
        var hotel = new ChromaHotel<string> { HotelId = "h1", Tags = [], Embedding = new float[] { 1, 2, 3, 4 } };

        // Act.
        var metadata = sut.MapFromDataToStorageModel(hotel, 0, generatedEmbeddings: null).Metadata!;

        // Assert.
        Assert.True(metadata.ContainsKey("Tags"));
        Assert.Null(metadata["Tags"]);
    }

    [Fact]
    public void WritesNoDocumentForANullFullTextProperty()
    {
        // Arrange: the collection empties the document of a record that exists.
        var sut = new ChromaMapper<FullTextHotel>(ChromaTestModel.Build<FullTextHotel>());
        var hotel = new FullTextHotel { HotelId = "h1", Description = null, Embedding = new float[] { 1, 2, 3, 4 } };

        // Act.
        var record = sut.MapFromDataToStorageModel(hotel, 0, generatedEmbeddings: null);

        // Assert.
        Assert.Null(record.Document);
        Assert.False(record.Metadata!.ContainsKey("Description"));
    }

    [Fact]
    public void ReadsTheDocumentAsTheClientGivesIt()
    {
        var sut = new ChromaMapper<FullTextHotel>(ChromaTestModel.Build<FullTextHotel>());
        var copy = new Dictionary<string, object> { ["Description"] = "A copy" };

        Assert.Equal("", sut.MapFromStorageToDataModel("h1", embedding: null, metadata: copy, document: "", includeVectors: false).Description);
        Assert.Null(sut.MapFromStorageToDataModel("h1", embedding: null, metadata: copy, document: null, includeVectors: false).Description);
    }

    [Fact]
    public void WritesTheFullTextPropertyAsTheDocument()
    {
        // Arrange.
        var sut = new ChromaMapper<FullTextHotel>(ChromaTestModel.Build<FullTextHotel>());
        var hotel = new FullTextHotel { HotelId = "h1", Description = "A pool and a spa", Embedding = new float[] { 1, 2, 3, 4 } };

        // Act.
        var record = sut.MapFromDataToStorageModel(hotel, 0, generatedEmbeddings: null);

        // Assert.
        Assert.Equal("A pool and a spa", record.Document);
        Assert.False(record.Metadata!.ContainsKey("Description"));
    }

    [Fact]
    public void ReadsTheFullTextPropertyFromTheDocumentWhenTheMetadataLacksIt()
    {
        // Arrange: a record written by another Chroma client, with its text in the document only.
        var sut = new ChromaMapper<FullTextHotel>(ChromaTestModel.Build<FullTextHotel>());

        // Act.
        var hotel = sut.MapFromStorageToDataModel("h1", embedding: null, metadata: null, document: "A pool and a spa", includeVectors: false);

        // Assert.
        Assert.Equal("A pool and a spa", hotel.Description);
    }

    [Fact]
    public void WritesNoDocumentWithoutAFullTextProperty()
        => Assert.Null(new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>())
            .MapFromDataToStorageModel(new ChromaHotel<string> { HotelId = "h1", HotelName = "Grand", Embedding = new float[] { 1, 2, 3, 4 } }, 0, generatedEmbeddings: null)
            .Document);

    [Fact]
    public void WritesNoDocumentWithTwoFullTextProperties()
        => Assert.Null(new ChromaMapper<TwoFullTextHotel>(ChromaTestModel.Build<TwoFullTextHotel>())
            .MapFromDataToStorageModel(new TwoFullTextHotel { HotelId = "h1", Description = "A pool", Review = "Great", Embedding = new float[] { 1, 2, 3, 4 } }, 0, generatedEmbeddings: null)
            .Document);

    [Fact]
    public void ThrowsWhenTheVectorIsMissing()
    {
        // Arrange.
        var sut = new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>());
        var hotel = new ChromaHotel<string> { HotelId = "h1" };

        // Act and assert.
        Assert.Throws<InvalidOperationException>(() => sut.MapFromDataToStorageModel(hotel, 0, generatedEmbeddings: null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapsAChromaRecordToTheDataModel(bool includeVectors)
    {
        // Arrange.
        var sut = new ChromaMapper<ChromaHotel<Guid>>(ChromaTestModel.Build<ChromaHotel<Guid>>());
        var metadata = new Dictionary<string, object> { ["HotelName"] = "Grand", ["Rating"] = 5L, ["Price"] = 120.5, ["parking_is_included"] = true };

        // Act.
        var hotel = sut.MapFromStorageToDataModel("11111111-1111-1111-1111-111111111111", new float[] { 1, 2, 3, 4 }, metadata, document: null, includeVectors);

        // Assert.
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), hotel.HotelId);
        Assert.Equal("Grand", hotel.HotelName);
        Assert.Equal(5, hotel.Rating);
        Assert.Equal(120.5, hotel.Price);
        Assert.True(hotel.Parking);
        Assert.Equal(includeVectors, hotel.Embedding.HasValue);
    }

    [Fact]
    public void ThrowsWhenTheKeyIsMissing()
    {
        var sut = new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>());

        Assert.Throws<InvalidOperationException>(() => sut.MapFromDataToStorageModel(new ChromaHotel<string> { HotelId = null!, Embedding = new float[] { 1, 2, 3, 4 } }, 0, generatedEmbeddings: null));
    }

    [Fact]
    public void WritesAndReadsAnEmbeddingVector()
    {
        // Arrange.
        var sut = new ChromaMapper<EmbeddingHotel>(ChromaTestModel.Build<EmbeddingHotel>());

        // Act.
        var storageRecord = sut.MapFromDataToStorageModel(new EmbeddingHotel { HotelId = "h1", Embedding = new Embedding<float>(new float[] { 1, 2, 3, 4 }) }, 0, generatedEmbeddings: null);
        var hotel = sut.MapFromStorageToDataModel("h1", new float[] { 1, 2, 3, 4 }, metadata: null, document: null, includeVectors: true);

        // Assert.
        Assert.Equal(new float[] { 1, 2, 3, 4 }, storageRecord.Embedding.ToArray());
        Assert.Null(storageRecord.Metadata);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, hotel.Embedding!.Vector.ToArray());
    }

    [Fact]
    public void WritesAndReadsAnArrayVector()
    {
        // Arrange.
        var sut = new ChromaMapper<ArrayHotel>(ChromaTestModel.Build<ArrayHotel>());

        // Act.
        var storageRecord = sut.MapFromDataToStorageModel(new ArrayHotel { HotelId = "h1", Embedding = [1, 2, 3, 4] }, 0, generatedEmbeddings: null);
        var hotel = sut.MapFromStorageToDataModel("h1", new float[] { 1, 2, 3, 4 }, metadata: null, document: null, includeVectors: true);

        // Assert.
        Assert.Equal(new float[] { 1, 2, 3, 4 }, storageRecord.Embedding.ToArray());
        Assert.Equal(new float[] { 1, 2, 3, 4 }, hotel.Embedding);
    }

    [Fact]
    public void WritesTheGeneratedEmbeddingOfTheRecord()
    {
        // Arrange.
        var sut = new ChromaMapper<TextHotel>(ChromaTestModel.Build<TextHotel>(new FakeEmbeddingGenerator()));
        var generated = new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 1, 2, 3, 4 }), new Embedding<float>(new float[] { 5, 6, 7, 8 })]);

        // Act.
        var storageRecord = sut.MapFromDataToStorageModel(new TextHotel { HotelId = "h2", Embedding = "a spa" }, 1, [generated]);

        // Assert.
        Assert.Equal(new float[] { 5, 6, 7, 8 }, storageRecord.Embedding.ToArray());
    }

    [Fact]
    public void ReadsNoVectorWhenChromaReturnsNone()
    {
        var sut = new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>());

        Assert.Null(sut.MapFromStorageToDataModel("h1", embedding: null, metadata: null, document: null, includeVectors: true).Embedding);
    }

    public static TheoryData<string, object> ValuesThatCannotBeRead => new()
    {
        // Another type, as another Chroma client may write under the key of a property.
        { "Rating", "five" },
        // A number too large for the property.
        { "Rating", long.MaxValue },
    };

    [Theory]
    [MemberData(nameof(ValuesThatCannotBeRead))]
    public void ThrowsAnErrorThatNamesThePropertyAndTheRecordForAValueThatCannotBeRead(string key, object value)
    {
        // Arrange.
        var sut = new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>());

        // Act.
        var exception = Assert.Throws<InvalidOperationException>(() => sut.MapFromStorageToDataModel("h1", embedding: null, new Dictionary<string, object> { [key] = value }, document: null, includeVectors: false));

        // Assert.
        Assert.Contains($"'{key}'", exception.Message);
        Assert.Contains("'h1'", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void ThrowsAnErrorThatNamesThePropertyForADateThatDoesNotParse()
    {
        // Arrange.
        var sut = new ChromaMapper<DatedHotel>(ChromaTestModel.Build<DatedHotel>());

        // Act.
        var exception = Assert.Throws<InvalidOperationException>(() => sut.MapFromStorageToDataModel("h1", embedding: null, new Dictionary<string, object> { ["Opened"] = "not a date" }, document: null, includeVectors: false));

        // Assert.
        Assert.Contains("'Opened'", exception.Message);
        Assert.IsType<FormatException>(Assert.IsType<InvalidCastException>(exception.InnerException).InnerException);
    }

    [Fact]
    public void ReadsAMissingMetadataValueAsNull()
    {
        // Arrange.
        var sut = new ChromaMapper<ChromaHotel<string>>(ChromaTestModel.Build<ChromaHotel<string>>());

        // Act.
        var hotel = sut.MapFromStorageToDataModel("h1", embedding: null, new Dictionary<string, object> { ["Price"] = 10.0 }, document: null, includeVectors: false);

        // Assert.
        Assert.Null(hotel.HotelName);
        Assert.Null(hotel.Rating);
        Assert.Null(hotel.Tags);
        Assert.Equal(10.0, hotel.Price);
    }
}
