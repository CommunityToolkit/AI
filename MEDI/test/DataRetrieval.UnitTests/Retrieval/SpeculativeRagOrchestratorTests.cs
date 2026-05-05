using Microsoft.Extensions.DataRetrieval;
using CommunityToolkit.DataRetrieval.UnitTests.Utils;

namespace CommunityToolkit.DataRetrieval.UnitTests.Retrieval;

public class SpeculativeRagOrchestratorTests
{
    [Fact]
    public void Constructor_ThrowsOnNullDrafterClient()
    {
        using var verifier = TestChatClient.WithJsonResponse("{}");
        Assert.Throws<ArgumentNullException>("drafterClient",
            () => new SpeculativeRagOrchestrator(null!, verifier));
    }

    [Fact]
    public void Constructor_ThrowsOnNullVerifierClient()
    {
        using var drafter = TestChatClient.WithJsonResponse("{}");
        Assert.Throws<ArgumentNullException>("verifierClient",
            () => new SpeculativeRagOrchestrator(drafter, null!));
    }

    [Fact]
    public async Task GenerateAsync_ProducesDraftsInParallel()
    {
        using var drafter = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        "Draft answer about the topic.")))
        };
        using var verifier = TestChatClient.WithJsonResponse(
            """{"bestDraftIndex": 1, "confidence": 0.9, "reasoning": "draft 1 is best"}""");

        var collection = new TestVectorStoreCollection();
        collection.AddSearchResult("chunk1", 0.9);
        collection.AddSearchResult("chunk2", 0.8);

        var orchestrator = new SpeculativeRagOrchestrator(drafter, verifier)
        {
            DraftCount = 3
        };
        var result = await orchestrator.GenerateAsync(
            collection, "test query",
            r => (string)r["content"]!);

        Assert.Equal(3, result.Drafts.Count);
        Assert.NotEmpty(result.Answer);
        Assert.True(result.ParallelDraftMs >= 0);
    }

    [Fact]
    public async Task GenerateAsync_SelectsBestDraft()
    {
        int draftIndex = 0;
        using var drafter = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
            {
                var idx = Interlocked.Increment(ref draftIndex);
                return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        $"Draft {idx} answer.")));
            }
        };
        using var verifier = TestChatClient.WithJsonResponse(
            """{"bestDraftIndex": 2, "confidence": 0.85, "reasoning": "draft 2 most accurate"}""");

        var collection = new TestVectorStoreCollection();
        collection.AddSearchResult("context", 0.9);

        var orchestrator = new SpeculativeRagOrchestrator(drafter, verifier) { DraftCount = 3 };
        var result = await orchestrator.GenerateAsync(
            collection, "query",
            r => (string)r["content"]!);

        Assert.Equal(2, result.BestDraftIndex);
        Assert.Equal(0.85, result.Confidence);
    }

    [Fact]
    public async Task GenerateAsync_VerificationFails_FallsBackToFirstDraft()
    {
        using var drafter = new TestChatClient
        {
            GetResponseAsyncCallback = (_, _, _) =>
                Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        "Fallback draft.")))
        };
        using var verifier = TestChatClient.WithException(new InvalidOperationException("verification failed"));

        var collection = new TestVectorStoreCollection();
        collection.AddSearchResult("context", 0.9);

        var orchestrator = new SpeculativeRagOrchestrator(drafter, verifier) { DraftCount = 2 };
        var result = await orchestrator.GenerateAsync(
            collection, "query",
            r => (string)r["content"]!);

        Assert.Equal(1, result.BestDraftIndex);
        Assert.Equal(0.5, result.Confidence);
        Assert.Equal("verification failed", result.VerificationReasoning);
    }
}
