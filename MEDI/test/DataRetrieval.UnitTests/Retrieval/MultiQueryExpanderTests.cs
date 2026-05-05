using Microsoft.Extensions.DataRetrieval;
using CommunityToolkit.DataRetrieval.UnitTests.Utils;

namespace CommunityToolkit.DataRetrieval.UnitTests.Retrieval;

public class MultiQueryExpanderTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient", () => new MultiQueryExpander(null!));
    }

    [Fact]
    public async Task ProcessAsync_AddsVariantsAfterOriginal()
    {
        using var client = TestChatClient.WithJsonResponse(
            """{"variants": ["how to configure RAG", "RAG pipeline setup guide", "setting up retrieval augmented generation"]}""");
        var expander = new MultiQueryExpander(client);
        var query = new RetrievalQuery("how to set up RAG");

        var output = await expander.ProcessAsync(query);

        Assert.Equal("how to set up RAG", output.Variants[0]); // Original first
        Assert.True(output.Variants.Count >= 2); // At least original + 1 variant
    }

    [Fact]
    public async Task ProcessAsync_FiltersShortVariants()
    {
        using var client = TestChatClient.WithJsonResponse(
            """{"variants": ["short", "", "this is a valid query variant about RAG"]}""");
        var expander = new MultiQueryExpander(client);
        var query = new RetrievalQuery("original query text");

        var output = await expander.ProcessAsync(query);

        // "short" (5 chars) and "" are filtered out, only the valid variant remains
        Assert.Equal(2, output.Variants.Count); // original + 1 valid
        Assert.DoesNotContain("short", output.Variants);
    }

    [Fact]
    public async Task ProcessAsync_OriginalAlwaysFirst()
    {
        using var client = TestChatClient.WithJsonResponse(
            """{"variants": ["variant one that is long enough", "variant two that is long enough"]}""");
        var expander = new MultiQueryExpander(client);
        var query = new RetrievalQuery("my original question");

        var output = await expander.ProcessAsync(query);

        Assert.Equal("my original question", output.Variants[0]);
    }

    [Fact]
    public async Task ProcessAsync_LlmThrows_ReturnsOriginalOnly()
    {
        using var client = TestChatClient.WithException(new InvalidOperationException("fail"));
        var expander = new MultiQueryExpander(client);
        var query = new RetrievalQuery("my question");

        var output = await expander.ProcessAsync(query);

        Assert.Single(output.Variants);
        Assert.Equal("my question", output.Variants[0]);
    }

    [Fact]
    public async Task ProcessAsync_LimitsToVariantCount()
    {
        using var client = TestChatClient.WithJsonResponse(
            """{"variants": ["variant one is long enough", "variant two is long enough", "variant three is long enough", "variant four is long enough", "variant five is long enough"]}""");
        var expander = new MultiQueryExpander(client) { VariantCount = 2 };
        var query = new RetrievalQuery("test");

        var output = await expander.ProcessAsync(query);

        // 1 original + at most 2 variants
        Assert.True(output.Variants.Count <= 3);
    }
}
