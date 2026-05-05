using Microsoft.Extensions.DataRetrieval;
using CommunityToolkit.DataRetrieval.UnitTests.Utils;

namespace CommunityToolkit.DataRetrieval.UnitTests.Retrieval;

public class SelfRagOrchestratorTests
{
    [Fact]
    public void Constructor_ThrowsOnNullChatClient()
    {
        Assert.Throws<ArgumentNullException>("chatClient", () => new SelfRagOrchestrator(null!));
    }

    [Fact]
    public async Task GenerateAsync_NeedsRetrieval_RetrievesChunks()
    {
        int callCount = 0;
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, ct) =>
            {
                callCount++;
                string json = callCount switch
                {
                    1 => """{"needsRetrieval": true, "reasoning": "technical question"}""",
                    2 => "The answer is based on retrieved context.",
                    3 => """{"relevance": 5, "faithfulness": 5, "critique": "excellent"}""",
                    _ => "{}"
                };
                return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant, json)));
            }
        };

        var collection = new TestVectorStoreCollection();
        collection.AddSearchResult("relevant chunk", 0.9);

        var orchestrator = new SelfRagOrchestrator(client);
        var result = await orchestrator.GenerateAsync(
            collection, "How does DI work?",
            r => (string)r["content"]!);

        Assert.True(result.NeedsRetrieval);
        Assert.NotEmpty(result.RetrievedChunks);
        Assert.NotEmpty(result.Answer);
    }

    [Fact]
    public async Task GenerateAsync_NoRetrieval_GeneratesFromKnowledge()
    {
        int callCount = 0;
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, ct) =>
            {
                callCount++;
                string json = callCount switch
                {
                    1 => """{"needsRetrieval": false, "reasoning": "general knowledge"}""",
                    2 => "The answer from general knowledge.",
                    3 => """{"relevance": 4, "faithfulness": 4, "critique": "good"}""",
                    _ => "{}"
                };
                return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant, json)));
            }
        };

        var collection = new TestVectorStoreCollection();
        var orchestrator = new SelfRagOrchestrator(client);
        var result = await orchestrator.GenerateAsync(
            collection, "What is water?",
            r => (string)r["content"]!);

        Assert.False(result.NeedsRetrieval);
        Assert.Empty(result.RetrievedChunks);
    }

    [Fact]
    public async Task GenerateAsync_LowScore_RetriesWithForcedRetrieval()
    {
        int callCount = 0;
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, ct) =>
            {
                callCount++;
                string json = callCount switch
                {
                    1 => """{"needsRetrieval": false, "reasoning": "try without"}""",
                    2 => "Weak answer.",
                    3 => """{"relevance": 1, "faithfulness": 1, "critique": "poor"}""",
                    4 => "Better answer with context.",
                    5 => """{"relevance": 4, "faithfulness": 4, "critique": "improved"}""",
                    _ => "{}"
                };
                return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant, json)));
            }
        };

        var collection = new TestVectorStoreCollection();
        collection.AddSearchResult("helpful context", 0.9);

        var orchestrator = new SelfRagOrchestrator(client) { AcceptanceThreshold = 3.0 };
        var result = await orchestrator.GenerateAsync(
            collection, "complex question",
            r => (string)r["content"]!);

        Assert.True(callCount >= 4, "Should have retried after low score");
    }

    [Fact]
    public async Task GenerateAsync_MaxRetriesZero_NoRetryEvenIfLowScore()
    {
        int callCount = 0;
        using var client = new TestChatClient
        {
            GetResponseAsyncCallback = (messages, options, ct) =>
            {
                callCount++;
                string json = callCount switch
                {
                    1 => """{"needsRetrieval": false, "reasoning": "try"}""",
                    2 => "Bad answer.",
                    3 => """{"relevance": 1, "faithfulness": 1, "critique": "terrible"}""",
                    _ => "{}"
                };
                return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant, json)));
            }
        };

        var collection = new TestVectorStoreCollection();
        var orchestrator = new SelfRagOrchestrator(client) { MaxRetries = 0 };
        var result = await orchestrator.GenerateAsync(
            collection, "test",
            r => (string)r["content"]!);

        Assert.Equal(3, callCount); // No retry calls
        Assert.Equal(1.0, result.AverageScore);
    }
}
