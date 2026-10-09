// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using CommunityToolkit.VectorData.Chroma;
using InMemory.UnitTests;
using Microsoft.Extensions.VectorData;
using Moq;
using Moq.Protected;
using Xunit;

namespace Chroma.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChromaCollection{TKey, TRecord}"/> class.
/// </summary>
public class ChromaCollectionTests
{
    private const string TestCollectionName = "testcollection";

    private readonly Mock<ChromaClient> _chromaClientMock = new(MockBehavior.Strict);
    private readonly Mock<ChromaCollectionClient> _collectionClientMock = new(MockBehavior.Strict);

    // A token that is not the default one, so that the strict mocks also check that it reaches the client.
    private readonly CancellationToken _testCancellationToken = TestContext.Current.CancellationToken;

    public ChromaCollectionTests()
    {
        this._chromaClientMock
            .Setup(x => x.Options)
            .Returns(new ChromaConfigurationOptions("http://localhost:8000"));
        this._chromaClientMock
            .Setup(x => x.GetCollectionClient(TestCollectionName))
            .Returns(this._collectionClientMock.Object);
        this._collectionClientMock
            .Setup(x => x.WithDocumentCopyKey(It.IsAny<string>()))
            .Returns(this._collectionClientMock.Object);
        this._collectionClientMock
            .Setup(x => x.WithMetadataValues(ChromaMetadataValues.Exact))
            .Returns(this._collectionClientMock.Object);
    }

    #region Construction and services

    [Fact]
    public void RejectsADictionaryRecord()
        => Assert.Throws<NotSupportedException>(() => new ChromaCollection<object, Dictionary<string, object?>>(this._chromaClientMock.Object, TestCollectionName, ownsClient: false));

    [Fact]
    public void DynamicCollectionsTakeAClientAndADefinition()
    {
        // Arrange.
        var definition = new VectorStoreCollectionDefinition
        {
            Properties = [new VectorStoreKeyProperty("Key", typeof(string)), new VectorStoreVectorProperty("Vector", typeof(ReadOnlyMemory<float>), 4)]
        };

        // Act.
        using var sut = new ChromaDynamicCollection(this._chromaClientMock.Object, TestCollectionName, ownsClient: false, new() { Definition = definition });

        // Assert.
        Assert.Equal(TestCollectionName, sut.Name);
        Assert.Same(this._chromaClientMock.Object, sut.GetService(typeof(ChromaClient)));
    }

    [Fact]
    public void ReadsTheRecordsWithExactMetadataValues()
    {
        // The mapper reads strings as strings, and lists as lists of values, also with a client that infers them.
        this._chromaClientMock
            .Setup(x => x.Options)
            .Returns(new ChromaConfigurationOptions("http://localhost:8000").WithMetadataValues(ChromaMetadataValues.Inferred));

        using var sut = new ChromaCollection<string, ChromaHotel<string>>(this._chromaClientMock.Object, TestCollectionName, ownsClient: false);

        this._collectionClientMock.Verify(x => x.WithMetadataValues(ChromaMetadataValues.Exact), Times.Once);
    }

    [Fact]
    public void DynamicCollectionsRequireADefinition()
        => Assert.Throws<ArgumentException>(() => new ChromaDynamicCollection(this._chromaClientMock.Object, TestCollectionName, ownsClient: false, new()));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisposeDisposesTheClientOnlyWhenTheCollectionOwnsIt(bool ownsClient)
    {
        // Arrange.
        this._chromaClientMock.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        var sut = new ChromaCollection<string, ChromaHotel<string>>(this._chromaClientMock.Object, TestCollectionName, ownsClient);

        // Act.
        sut.Dispose();

        // Assert.
        this._chromaClientMock.Protected().Verify("Dispose", ownsClient ? Times.Once() : Times.Never(), ItExpr.IsAny<bool>());
    }

    [Fact]
    public void GetServiceReturnsTheMetadataTheClientAndTheCollection()
    {
        using var sut = this.CreateCollection<string, ChromaHotel<string>>();

        Assert.Equal(TestCollectionName, Assert.IsType<VectorStoreCollectionMetadata>(sut.GetService(typeof(VectorStoreCollectionMetadata))).CollectionName);
        Assert.Same(this._chromaClientMock.Object, sut.GetService(typeof(ChromaClient)));
        Assert.Same(sut, sut.GetService(typeof(VectorStoreCollection<string, ChromaHotel<string>>)));
        Assert.Null(sut.GetService(typeof(VectorStoreCollection<string, ChromaHotel<string>>), "key"));
        Assert.Null(sut.GetService(typeof(string)));
    }

    [Theory]
    [InlineData("https://api.trychroma.com", true)]
    [InlineData("http://localhost:8000", false)]
    public void GetServiceOffersHybridSearchOnlyWithFullTextPropertiesOnChromaCloud(string uri, bool offered)
    {
        // Arrange.
        this._chromaClientMock
            .Setup(x => x.Options)
            .Returns(new ChromaConfigurationOptions(uri));
        using var fullText = this.CreateCollection<string, FullTextHotel>();
        using var noFullText = this.CreateCollection<string, ChromaHotel<string>>();

        // Act and assert.
        Assert.Equal(offered, fullText.GetService(typeof(IKeywordHybridSearchable<FullTextHotel>)) is not null);
        Assert.Null(noFullText.GetService(typeof(IKeywordHybridSearchable<ChromaHotel<string>>)));
    }

    #endregion

    #region Get by key

    [Fact]
    public async Task GetReadsTheVectorsAndTheDocumentWhenAskedAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, FullTextHotel>();
        this._collectionClientMock
            .Setup(x => x.GetAsync(new List<string> { "h1" }, null, null, null, null, ChromaGetInclude.Metadatas | ChromaGetInclude.Embeddings | ChromaGetInclude.Documents, this._testCancellationToken))
            .ReturnsAsync([new ChromaCollectionEntry("h1") { Embedding = new float[] { 1, 2, 3, 4 }, Document = "A pool" }]);

        // Act.
        var hotel = await sut.GetAsync("h1", new() { IncludeVectors = true }, this._testCancellationToken);

        // Assert.
        Assert.Equal(new float[] { 1, 2, 3, 4 }, hotel!.Embedding!.Value.ToArray());
        Assert.Equal("A pool", hotel.Description);
    }

    #endregion

    #region Upsert

    [Fact]
    public async Task UpsertDeletesTheNullValuesAndCopiesTheDocumentAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, FullTextHotel>();
        var upserted = this.CaptureUpsert();

        // Act.
        await sut.UpsertAsync(
        [
            new FullTextHotel { HotelId = "h1", Description = null, Rating = 4, Embedding = new float[] { 1, 2, 3, 4 } },
            new FullTextHotel { HotelId = "h2", Description = "A pool", Embedding = new float[] { 1, 2, 3, 4 } },
        ], this._testCancellationToken);

        // Assert.
        var records = upserted();
        Assert.True(records.NullDocumentsDelete);
        this._collectionClientMock.Verify(x => x.WithDocumentCopyKey("Description"), Times.Once);
        Assert.Equal(new[] { null, "A pool" }, records.Documents!.ToArray<string?>());
        Assert.All(records.Metadatas!, metadata => Assert.False(metadata!.ContainsKey("Description")));
        Assert.Equal(4, records.Metadatas![0]!["Rating"]);
        Assert.Equal(0, records.Metadatas[1]!["Rating"]);
    }

    #endregion

    #region Vector search

    [Fact]
    public async Task SearchSkipsConvertsAndFiltersTheResultsAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, ChromaHotel<string>>();
        var query = this.SetupQuery(
            new ChromaCollectionQueryEntry("kept") { Distance = 0.2f },
            new ChromaCollectionQueryEntry("too far") { Distance = 0.6f });

        // Act.
        var results = await sut.SearchAsync(new ReadOnlyMemory<float>([1, 2, 3, 4]), top: 2, new() { Skip = 1, ScoreThreshold = 0.5 }, this._testCancellationToken).ToListAsync();

        // Assert.
        Assert.Equal((2, 1), (query().NResults, query().Offset));
        Assert.Same(ChromaWhereOperator.All, query().Where);
        Assert.Equal(ChromaQueryInclude.Metadatas | ChromaQueryInclude.Distances, query().Include);
        var result = Assert.Single(results);
        Assert.Equal("kept", result.Record.HotelId);
        Assert.Equal(0.8, result.Score!.Value, precision: 6);
    }

    [Fact]
    public async Task SearchSendsTheKeysOfTheFilterInTheWhereClauseAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, ChromaHotel<string>>();
        var query = this.SetupQuery(new ChromaCollectionQueryEntry("h1") { Distance = 0.1f });

        // Act.
        var results = await sut.SearchAsync(new ReadOnlyMemory<float>([1, 2, 3, 4]), top: 2, new() { Filter = h => new[] { "h1", "h2" }.Contains(h.HotelId) }, this._testCancellationToken).ToListAsync();

        // Assert.
        Assert.Equal("""{"#id":{"$in":["h1","h2"]}}""", query().Where!.ToString());
        Assert.Equal("h1", Assert.Single(results).Record.HotelId);
    }

    [Fact]
    public async Task SearchReadsTheVectorsAndTheDocumentWhenAskedAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, FullTextHotel>();
        var query = this.SetupQuery(new ChromaCollectionQueryEntry("h1") { Distance = 0.1f, Embedding = new float[] { 1, 2, 3, 4 }, Document = "A pool" });

        // Act.
        var result = Assert.Single(await sut.SearchAsync(new float[] { 1, 2, 3, 4 }, top: 1, new() { IncludeVectors = true }, this._testCancellationToken).ToListAsync());

        // Assert.
        Assert.Equal(ChromaQueryInclude.Metadatas | ChromaQueryInclude.Distances | ChromaQueryInclude.Documents | ChromaQueryInclude.Embeddings, query().Include);
        Assert.Equal("A pool", result.Record.Description);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, result.Record.Embedding!.Value.ToArray());
    }

    [Fact]
    public async Task SearchExpectsTheSpaceOfTheDistanceFunctionAsync()
    {
        // Arrange.
        using var dotProduct = this.CreateCollection<string, DotProductHotel>();
        using var byDefault = this.CreateCollection<string, ChromaHotel<string>>();
        var query = this.SetupQuery();

        // Act and assert.
        await dotProduct.SearchAsync(new ReadOnlyMemory<float>([1, 2, 3, 4]), top: 1, cancellationToken: this._testCancellationToken).ToListAsync();
        Assert.Equal(ChromaSpace.InnerProduct, query().ExpectedSpace);
        await byDefault.SearchAsync(new ReadOnlyMemory<float>([1, 2, 3, 4]), top: 1, cancellationToken: this._testCancellationToken).ToListAsync();
        Assert.Equal(ChromaSpace.Cosine, query().ExpectedSpace);
    }

    #endregion

    #region Hybrid search

    [Fact]
    public async Task HybridSearchSendsTheRrfOfTheVectorAndBm25SearchesAsync()
    {
        // Arrange: the collection has the BM25 index the provider creates.
        using var sut = this.CreateHybridCollection<FullTextHotel>(Bm25Index("Description_bm25", "Description"));
        ChromaSearch? search = null;
        this._collectionClientMock
            .Setup(x => x.SearchAsync(It.IsAny<ChromaSearch>(), null, this._testCancellationToken))
            .Callback<ChromaSearch, ChromaReadLevel?, CancellationToken>((s, _, _) => search = s)
            .ReturnsAsync(
            [
                new ChromaSearchEntry("h1") { Score = 0.032f, Document = "A pool and a spa", Metadata = new Dictionary<string, object> { ["Description"] = "A pool and a spa", ["Rating"] = 5L } },
                new ChromaSearchEntry("h2") { Score = 0.016f, Document = "A gym", Metadata = new Dictionary<string, object> { ["Description"] = "A gym", ["Rating"] = 4L } },
            ]);

        // Act.
        var results = await sut.HybridSearchAsync(new ReadOnlyMemory<float>([1, 2, 3, 4]), ["pool", "spa"], top: 2, new() { Skip = 1, Filter = h => h.Rating >= 4, ScoreThreshold = 0.02 }, this._testCancellationToken).ToListAsync();

        // Assert.
        Assert.Equal(2, search!.Limit);
        Assert.Equal(1, search.Offset);
        Assert.Equal("""{"Rating":{"$gte":4}}""", search.Where!.ToString());
        Assert.Equal([ChromaSearchKeys.Metadata, ChromaSearchKeys.Score, ChromaSearchKeys.Document], search.Select);

        // The threshold applies to the RRF score.
        var result = Assert.Single(results);
        Assert.Equal("h1", result.Record.HotelId);
        Assert.Equal("A pool and a spa", result.Record.Description);
        Assert.Equal(0.032, result.Score!.Value, precision: 6);
    }

    [Fact]
    public async Task HybridSearchReadsTheVectorsWhenAskedAsync()
    {
        // Arrange.
        using var sut = this.CreateHybridCollection<FullTextHotel>(Bm25Index("Description_bm25", "Description"));
        ChromaSearch? search = null;
        this._collectionClientMock
            .Setup(x => x.SearchAsync(It.IsAny<ChromaSearch>(), null, this._testCancellationToken))
            .Callback<ChromaSearch, ChromaReadLevel?, CancellationToken>((s, _, _) => search = s)
            .ReturnsAsync([new ChromaSearchEntry("h1") { Score = 0.032f, Embedding = new float[] { 1, 2, 3, 4 } }]);

        // Act.
        var result = Assert.Single(await sut.HybridSearchAsync(new float[] { 1, 2, 3, 4 }, ["pool"], top: 1, new() { IncludeVectors = true }, this._testCancellationToken).ToListAsync());

        // Assert.
        Assert.Contains(ChromaSearchKeys.Embedding, search!.Select!);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, result.Record.Embedding!.Value.ToArray());
    }

    [Fact]
    public async Task HybridSearchThrowsWithoutABm25IndexAsync()
    {
        // Arrange: the strict mock fails on any search.
        using var sut = this.CreateHybridCollection<FullTextHotel>();

        // Act and assert.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.HybridSearchAsync(new ReadOnlyMemory<float>([1, 2, 3, 4]), ["pool"], top: 1, cancellationToken: this._testCancellationToken).ToListAsync());
        Assert.Contains("Chroma Cloud", exception.Message);
    }

    [Fact]
    public async Task HybridSearchThrowsWithoutAnIndexOnTheChosenPropertyAsync()
    {
        // Arrange: an index on the other full-text property only.
        using var sut = this.CreateHybridCollection<TwoFullTextHotel>(Bm25Index("Description_bm25", "Description"));

        // Act and assert.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.HybridSearchAsync(new ReadOnlyMemory<float>([1, 2, 3, 4]), ["great"], top: 1, new() { AdditionalProperty = h => h.Review }, this._testCancellationToken).ToListAsync());
        Assert.Contains("'Review'", exception.Message);
    }

    [Fact]
    public async Task HybridSearchCannotReadVectorsWithEmbeddingGenerationAsync()
    {
        // Arrange.
        using var sut = new ChromaCollection<string, FullTextTextHotel>(this._chromaClientMock.Object, TestCollectionName, ownsClient: false, new ChromaCollectionOptions { EmbeddingGenerator = new FakeEmbeddingGenerator() });

        // Act and assert.
        await Assert.ThrowsAsync<NotSupportedException>(async () => await sut.HybridSearchAsync("a pool", ["pool"], top: 1, new() { IncludeVectors = true }, this._testCancellationToken).ToListAsync());
    }

    #endregion

    #region Get with a filter

    [Fact]
    public async Task GetSendsTheKeysOfTheFilterInTheWhereClauseAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, ChromaHotel<string>>();
        this._collectionClientMock
            .Setup(x => x.GetAsync(
                null,
                It.Is<ChromaWhereOperator>(where => where.ToString() == """{"$and":[{"#id":{"$in":["h1"]}},{"parking_is_included":{"$eq":true}}]}"""),
                null,
                5,
                0,
                ChromaGetInclude.Metadatas,
                this._testCancellationToken))
            .ReturnsAsync([new ChromaCollectionEntry("h1")]);

        // Act.
        var results = await sut.GetAsync(h => h.HotelId == "h1" && h.Parking, top: 5, cancellationToken: this._testCancellationToken).ToListAsync();

        // Assert.
        Assert.Equal("h1", Assert.Single(results).HotelId);
    }

    [Fact]
    public async Task GetWithAFilterPassesTopAndSkipAndReadsTheVectorsWhenAskedAsync()
    {
        // Arrange: Chroma reads the records with a limit and an offset.
        using var sut = this.CreateCollection<string, ChromaHotel<string>>();
        this._collectionClientMock
            .Setup(x => x.GetAsync(null, It.IsNotNull<ChromaWhereOperator>(), null, 5, 1, ChromaGetInclude.Metadatas | ChromaGetInclude.Embeddings, this._testCancellationToken))
            .ReturnsAsync([new ChromaCollectionEntry("h1") { Embedding = new float[] { 1, 2, 3, 4 } }]);

        // Act.
        var hotel = Assert.Single(await sut.GetAsync(h => h.Parking, top: 5, new() { Skip = 1, IncludeVectors = true }, this._testCancellationToken).ToListAsync());

        // Assert.
        Assert.Equal(new float[] { 1, 2, 3, 4 }, hotel.Embedding!.Value.ToArray());
    }

    [Fact]
    public async Task TheTokenOfTheEnumeratorReachesChromaAsync()
    {
        // Arrange: a caller that passes a token to the method, and another one, cancelled, to the enumerator.
        using var methodSource = new CancellationTokenSource();
        using var enumeratorSource = new CancellationTokenSource();
        enumeratorSource.Cancel();
        var collection = HybridChromaCollection(Bm25Index("Description_bm25", "Description"));
        this._collectionClientMock
            .Setup(x => x.FindBm25IndexAsync(It.IsAny<string>(), It.Is<CancellationToken>(t => t.IsCancellationRequested)))
            .ReturnsAsync((string key, CancellationToken _) => collection.FindBm25Index(key));
        using var sut = this.CreateCollection<string, FullTextHotel>();
        this._collectionClientMock
            .Setup(x => x.GetAsync(It.IsAny<IReadOnlyList<string>>(), null, null, null, null, ChromaGetInclude.Metadatas | ChromaGetInclude.Documents, It.Is<CancellationToken>(t => t.IsCancellationRequested)))
            .ReturnsAsync([new ChromaCollectionEntry("h1")]);
        this._collectionClientMock
            .Setup(x => x.GetAsync(null, It.IsNotNull<ChromaWhereOperator>(), null, 2, 0, ChromaGetInclude.Metadatas | ChromaGetInclude.Documents, It.Is<CancellationToken>(t => t.IsCancellationRequested)))
            .ReturnsAsync([new ChromaCollectionEntry("h1")]);
        this._collectionClientMock
            .Setup(x => x.QueryAsync(It.IsAny<ChromaQuery>(), It.Is<CancellationToken>(t => t.IsCancellationRequested)))
            .ReturnsAsync(new List<IReadOnlyList<ChromaCollectionQueryEntry>> { new[] { new ChromaCollectionQueryEntry("h1") { Distance = 0.1f } } });
        this._collectionClientMock
            .Setup(x => x.SearchAsync(It.IsAny<ChromaSearch>(), null, It.Is<CancellationToken>(t => t.IsCancellationRequested)))
            .ReturnsAsync([new ChromaSearchEntry("h1") { Score = 0.032f }]);
        var vector = new float[] { 1, 2, 3, 4 };

        // Act.
        var byKeys = await ReadAllAsync(sut.GetAsync(["h1"], cancellationToken: methodSource.Token), enumeratorSource.Token);
        var byFilter = await ReadAllAsync(sut.GetAsync(h => h.Rating > 1, top: 2, cancellationToken: methodSource.Token), enumeratorSource.Token);
        var bySearch = await ReadAllAsync(sut.SearchAsync(vector, top: 2, cancellationToken: methodSource.Token), enumeratorSource.Token);
        var byHybridSearch = await ReadAllAsync(sut.HybridSearchAsync(vector, ["pool"], top: 2, cancellationToken: methodSource.Token), enumeratorSource.Token);

        // Assert: each request was sent with the cancelled token, which the mocks require.
        Assert.Single(byKeys);
        Assert.Single(byFilter);
        Assert.Single(bySearch);
        Assert.Single(byHybridSearch);

        static async Task<List<T>> ReadAllAsync<T>(IAsyncEnumerable<T> results, CancellationToken cancellationToken)
        {
            var list = new List<T>();
            await foreach (var result in results.WithCancellation(cancellationToken))
            {
                list.Add(result);
            }

            return list;
        }
    }

    #endregion

    #region Errors

    [Fact]
    public async Task ThrowsWhenTheCollectionIsMissingAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, ChromaHotel<string>>();
        this._collectionClientMock
            .Setup(x => x.GetAsync(It.IsAny<IReadOnlyList<string>>(), null, null, null, null, ChromaGetInclude.Metadatas, this._testCancellationToken))
            .ThrowsAsync(new ChromaException("Collection does not exist.") { StatusCode = HttpStatusCode.NotFound, ErrorType = "NotFoundError" });

        // Act and assert.
        await Assert.ThrowsAsync<VectorStoreException>(() => sut.GetAsync("h1", cancellationToken: this._testCancellationToken));
    }

    [Fact]
    public async Task WrapsChromaExceptionsInVectorStoreExceptionsAsync()
    {
        // Arrange.
        using var sut = this.CreateCollection<string, ChromaHotel<string>>();
        this._chromaClientMock
            .Setup(x => x.CollectionExistsAsync(TestCollectionName, null, null, this._testCancellationToken))
            .ThrowsAsync(new ChromaException("Unexpected status code"));

        // Act.
        var exception = await Assert.ThrowsAsync<VectorStoreException>(() => sut.CollectionExistsAsync(this._testCancellationToken));

        // Assert.
        Assert.Equal("chroma", exception.VectorStoreSystemName);
        Assert.IsType<ChromaException>(exception.InnerException);
    }

    #endregion

    private Func<ChromaRecords> CaptureUpsert()
    {
        ChromaRecords? records = null;
        this._collectionClientMock
            .Setup(x => x.UpsertAsync(It.IsAny<ChromaRecords>(), this._testCancellationToken))
            .Callback<ChromaRecords, CancellationToken>((r, _) => records = r)
            .Returns(Task.CompletedTask);
        return () => records!;
    }

    // A query of one embedding, which the client answers with the given records.
    private Func<ChromaQuery> SetupQuery(params ChromaCollectionQueryEntry[] results)
    {
        ChromaQuery? query = null;
        this._collectionClientMock
            .Setup(x => x.QueryAsync(It.IsAny<ChromaQuery>(), this._testCancellationToken))
            .Callback<ChromaQuery, CancellationToken>((q, _) => query = q)
            .ReturnsAsync(new List<IReadOnlyList<ChromaCollectionQueryEntry>> { results });
        return () => query!;
    }

    // The collection is not read.
    private ChromaCollection<TKey, TRecord> CreateCollection<TKey, TRecord>()
        where TKey : notnull
        where TRecord : class
        => new(this._chromaClientMock.Object, TestCollectionName, ownsClient: false);

    private ChromaCollection<string, TRecord> CreateHybridCollection<TRecord>(params string[] indexes)
        where TRecord : class
    {
        // The client finds the BM25 index of a key in the schema of the collection.
        var collection = HybridChromaCollection(indexes);
        this._collectionClientMock
            .Setup(x => x.FindBm25IndexAsync(It.IsAny<string>(), this._testCancellationToken))
            .ReturnsAsync((string key, CancellationToken _) => collection.FindBm25Index(key));

        return new(this._chromaClientMock.Object, TestCollectionName, ownsClient: false);
    }

    // A collection of Chroma Cloud with the given sparse vector indexes in its schema.
    private static ChromaCollection HybridChromaCollection(params string[] indexes)
        => new(TestCollectionName) { Id = Guid.NewGuid(), SchemaJson = JsonDocument.Parse("{\"keys\":{" + string.Join(",", indexes) + "}}").RootElement.Clone() };

    // A sparse vector index as Chroma Cloud returns it in the schema of a collection.
    private static string Bm25Index(string key, string sourceKey)
        => "\"" + key + "\":{\"sparse_vector\":{\"sparse_vector_index\":{\"enabled\":true,\"config\":{\"source_key\":\"" + sourceKey + "\",\"bm25\":true,"
            + "\"embedding_function\":{\"type\":\"known\",\"name\":\"chroma_bm25\",\"config\":{\"k\":1.2,\"b\":0.75,\"avg_doc_length\":256,\"token_max_length\":40}}}}}}";
}
