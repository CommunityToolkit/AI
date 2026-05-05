using Microsoft.Extensions.DataRetrieval;

namespace CommunityToolkit.DataRetrieval.UnitTests.Retrieval;

public class TreeSearchRetrieverTests
{
    [Fact]
    public async Task ProcessAsync_SetsTreeTraversalMetadata()
    {
        var retriever = new TreeSearchRetriever();
        var query = new RetrievalQuery("broad question about architecture");

        var output = await retriever.ProcessAsync(query);

        Assert.Equal("TreeTraversal", output.Metadata["search_paradigm"]);
    }

    [Fact]
    public async Task ProcessAsync_SetsResultsPerLevel()
    {
        var retriever = new TreeSearchRetriever { ResultsPerLevel = 5 };
        var query = new RetrievalQuery("test");

        var output = await retriever.ProcessAsync(query);

        Assert.Equal(5, output.Metadata["results_per_level"]);
    }

    [Fact]
    public async Task TraverseAsync_GroupsByLevelAndReturnsOrdered()
    {
        var collection = new Utils.TestVectorStoreCollection();
        collection.AddSearchResult("root summary", 0.7,
            new Dictionary<string, object?> { ["level"] = 2, ["id"] = "root", ["parent_id"] = "" });
        collection.AddSearchResult("branch summary", 0.8,
            new Dictionary<string, object?> { ["level"] = 1, ["id"] = "branch-1", ["parent_id"] = "root" });
        collection.AddSearchResult("leaf content", 0.9,
            new Dictionary<string, object?> { ["level"] = 0, ["id"] = "leaf-1", ["parent_id"] = "branch-1" });

        var results = await TreeSearchRetriever.TraverseAsync(
            collection,
            "test query",
            contentSelector: r => (string)r["content"]!,
            levelSelector: r => (int)r["level"]!,
            parentIdSelector: r => r["parent_id"] as string,
            idSelector: r => r["id"] as string,
            topK: 5);

        Assert.True(results.Count > 0);
        // Results should be ordered by score descending
        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Score >= results[i].Score);
        }
    }
}
