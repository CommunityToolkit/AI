// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using CommunityToolkit.VectorData.Chroma;
using Moq;
using Moq.Protected;
using Xunit;

namespace Chroma.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChromaVectorStore"/> class.
/// </summary>
public class ChromaVectorStoreTests
{
    private const string TestCollectionName = "testcollection";

    private readonly Mock<ChromaClient> _chromaClientMock = new(MockBehavior.Strict);

    // A token that is not the default one, so that the strict mocks also check that it reaches the client.
    private readonly CancellationToken _testCancellationToken = TestContext.Current.CancellationToken;

    public ChromaVectorStoreTests()
    {
        this._chromaClientMock
            .Setup(x => x.Options)
            .Returns(new ChromaConfigurationOptions("http://localhost:8000"));
        var collectionClientMock = new Mock<ChromaCollectionClient>(MockBehavior.Strict);
        collectionClientMock
            .Setup(x => x.WithMetadataValues(ChromaMetadataValues.Exact))
            .Returns(collectionClientMock.Object);
        this._chromaClientMock
            .Setup(x => x.GetCollectionClient(It.IsAny<string>()))
            .Returns(collectionClientMock.Object);
    }

    [Fact]
    public void GetCollectionReturnsChromaCollection()
    {
        // Arrange.
        using var sut = new ChromaVectorStore(this._chromaClientMock.Object, ownsClient: false);

        // Act.
        using var actual = sut.GetCollection<string, ChromaHotel<string>>(TestCollectionName);

        // Assert.
        Assert.IsType<ChromaCollection<string, ChromaHotel<string>>>(actual);
    }

    [Fact]
    public void GetCollectionThrowsForInvalidKeyType()
    {
        // Arrange.
        using var sut = new ChromaVectorStore(this._chromaClientMock.Object, ownsClient: false);

        // Act & Assert.
        Assert.Throws<NotSupportedException>(() => sut.GetCollection<int, ChromaHotel<int>>(TestCollectionName));
    }

    [Fact]
    public async Task ListCollectionNamesCallsClientAsync()
    {
        // Arrange.
        this._chromaClientMock
            .Setup(x => x.ListCollectionsAsync(null, null, this._testCancellationToken))
            .ReturnsAsync([new ChromaCollection("collection1"), new ChromaCollection("collection2")]);
        using var sut = new ChromaVectorStore(this._chromaClientMock.Object, ownsClient: false);

        // Act.
        var collectionNames = await sut.ListCollectionNamesAsync(this._testCancellationToken).ToListAsync();

        // Assert.
        Assert.Equal(["collection1", "collection2"], collectionNames);
    }

    [Fact]
    public void DisposingTwiceReleasesTheClientOnce()
    {
        // Arrange.
        this._chromaClientMock.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        var sut = new ChromaVectorStore(this._chromaClientMock.Object, ownsClient: true);
        var collection = sut.GetCollection<string, ChromaHotel<string>>(TestCollectionName);

        // Act: the collection still uses the client after the store is disposed.
        sut.Dispose();
        sut.Dispose();
        this._chromaClientMock.Protected().Verify("Dispose", Times.Never(), ItExpr.IsAny<bool>());
        collection.Dispose();
        collection.Dispose();

        // Assert.
        this._chromaClientMock.Protected().Verify("Dispose", Times.Once(), ItExpr.IsAny<bool>());
    }

    [Fact]
    public async Task CollectionExistsAndEnsureCollectionDeletedReleaseTheClientAsync()
    {
        // Arrange.
        this._chromaClientMock.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        this._chromaClientMock
            .Setup(x => x.CollectionExistsAsync(TestCollectionName, null, null, this._testCancellationToken))
            .ReturnsAsync(true);
        this._chromaClientMock
            .Setup(x => x.DeleteCollectionIfExistsAsync(TestCollectionName, null, null, true, this._testCancellationToken))
            .ReturnsAsync(true);
        var sut = new ChromaVectorStore(this._chromaClientMock.Object, ownsClient: true);

        // Act.
        Assert.True(await sut.CollectionExistsAsync(TestCollectionName, this._testCancellationToken));
        await sut.EnsureCollectionDeletedAsync(TestCollectionName, this._testCancellationToken);
        sut.Dispose();

        // Assert.
        this._chromaClientMock.Protected().Verify("Dispose", Times.Once(), ItExpr.IsAny<bool>());
    }
}
