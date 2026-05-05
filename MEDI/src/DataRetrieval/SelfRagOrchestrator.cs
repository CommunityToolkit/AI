using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.DataRetrieval;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Self-RAG orchestrator — adaptive retrieval with self-evaluation.
/// Flow: decide retrieval → generate → self-critique → accept or retry with forced retrieval.
/// </summary>
/// <remarks>
/// For compliance, medical, or legal domains where answer quality matters more than speed.
/// </remarks>
public class SelfRagOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    /// <summary>Minimum average score (relevance + faithfulness) to accept an answer.</summary>
    public double AcceptanceThreshold { get; init; } = 3.0;

    /// <summary>Maximum retry attempts before accepting best-effort.</summary>
    public int MaxRetries { get; init; } = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelfRagOrchestrator"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client for LLM inference.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public SelfRagOrchestrator(IChatClient chatClient, ILoggerFactory? loggerFactory = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = loggerFactory?.CreateLogger<SelfRagOrchestrator>();
    }

    /// <summary>
    /// Runs the Self-RAG flow for a query against a vector store collection.
    /// </summary>
    public async Task<SelfRagResult> GenerateAsync<TKey, TRecord>(
        VectorStoreCollection<TKey, TRecord> collection,
        string query,
        Func<TRecord, string> contentSelector,
        int topK = 3,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        where TRecord : class
    {
        var (needsRetrieval, reasoning) = await DecideRetrievalAsync(query, cancellationToken);
        _logger?.LogDebug("Self-RAG retrieval decision: {Decision} — {Reasoning}", needsRetrieval, reasoning);

        List<string>? retrievedChunks = null;

        if (needsRetrieval)
        {
            retrievedChunks = await RetrieveChunksAsync(collection, query, contentSelector, topK, cancellationToken);
        }

        var answer = await GenerateAnswerAsync(query, retrievedChunks, cancellationToken);

        var (relevance, faithfulness, critique) = await SelfCritiqueAsync(
            query, answer, retrievedChunks, cancellationToken);
        var avgScore = (relevance + faithfulness) / 2.0;

        _logger?.LogDebug("Self-RAG critique: relevance={Relevance}, faithfulness={Faithfulness}, avg={Avg}",
            relevance, faithfulness, avgScore);

        if (avgScore < AcceptanceThreshold && MaxRetries > 0)
        {
            _logger?.LogInformation("Self-RAG score {Score} below threshold {Threshold} — retrying with forced retrieval",
                avgScore, AcceptanceThreshold);

            retrievedChunks = await RetrieveChunksAsync(collection, query, contentSelector, topK, cancellationToken);
            answer = await GenerateAnswerAsync(query, retrievedChunks, cancellationToken);
            (relevance, faithfulness, critique) = await SelfCritiqueAsync(
                query, answer, retrievedChunks, cancellationToken);
            avgScore = (relevance + faithfulness) / 2.0;
        }

        return new SelfRagResult
        {
            Answer = answer,
            NeedsRetrieval = needsRetrieval,
            RetrievalReasoning = reasoning,
            Relevance = relevance,
            Faithfulness = faithfulness,
            AverageScore = avgScore,
            Critique = critique,
            RetrievedChunks = retrievedChunks ?? []
        };
    }

    private async Task<(bool NeedsRetrieval, string Reasoning)> DecideRetrievalAsync(
        string query, CancellationToken ct)
    {
        var prompt = "You are an AI assistant deciding whether external retrieval is needed to answer a query.\n\n" +
            "Rules:\n" +
            "- If the query asks about SPECIFIC technical details, APIs, or framework features → needsRetrieval = true\n" +
            "- If the query asks about GENERAL concepts that are widely known → needsRetrieval = false\n" +
            "- If borderline → needsRetrieval = true\n\n" +
            $"Query: \"{query}\"\n\n" +
            "Return ONLY valid JSON: {\"needsRetrieval\": true, \"reasoning\": \"one sentence\"}";

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt,
                new ChatOptions { MaxOutputTokens = 200, ResponseFormat = ChatResponseFormat.Json }, ct);
            var result = JsonSerializer.Deserialize<RetrievalDecisionResponse>(
                response.Text ?? "{}", JsonDefaults.Options);
            return (result?.NeedsRetrieval ?? true, result?.Reasoning ?? "");
        }
        catch
        {
            return (true, "defaulting to retrieval on failure");
        }
    }

    private static async Task<List<string>> RetrieveChunksAsync<TKey, TRecord>(
        VectorStoreCollection<TKey, TRecord> collection,
        string query, Func<TRecord, string> contentSelector,
        int topK, CancellationToken ct)
        where TKey : notnull
        where TRecord : class
    {
        var results = new List<string>();
        await foreach (var hit in collection.SearchAsync(query, top: topK, cancellationToken: ct))
        {
            if (hit.Record is not null)
                results.Add(contentSelector(hit.Record));
        }
        return results;
    }

    private async Task<string> GenerateAnswerAsync(
        string query, List<string>? chunks, CancellationToken ct)
    {
        string prompt;
        if (chunks is { Count: > 0 })
        {
            var context = string.Join("\n---\n", chunks);
            prompt = $"Using ONLY the following passages, answer the question concisely.\n\nPassages:\n{context}\n\nQuestion: {query}\n\nAnswer:";
        }
        else
        {
            prompt = $"Answer the following question concisely from your general knowledge.\n\nQuestion: {query}\n\nAnswer:";
        }

        var response = await _chatClient.GetResponseAsync(prompt,
            new ChatOptions { MaxOutputTokens = 500 }, ct);
        return response.Text ?? "";
    }

    private async Task<(int Relevance, int Faithfulness, string Critique)> SelfCritiqueAsync(
        string query, string answer, List<string>? chunks, CancellationToken ct)
    {
        var contextSection = chunks is { Count: > 0 }
            ? "Retrieved passages:\n" + string.Join("\n---\n", chunks) + "\n\n"
            : "";

        var prompt = "Score the following answer from 1 (worst) to 5 (best).\n\n" +
            $"Question: \"{query}\"\n\n{contextSection}" +
            $"Answer: \"{answer}\"\n\n" +
            "Dimensions:\n- relevance: Does it address the question?\n- faithfulness: Is it supported by the passages?\n\n" +
            "Return ONLY valid JSON: {\"relevance\": 4, \"faithfulness\": 5, \"critique\": \"summary\"}";

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt,
                new ChatOptions { MaxOutputTokens = 200, ResponseFormat = ChatResponseFormat.Json }, ct);
            var result = JsonSerializer.Deserialize<CritiqueResponse>(
                response.Text ?? "{}", JsonDefaults.Options);

            return (
                Math.Clamp(result?.Relevance ?? 3, 1, 5),
                Math.Clamp(result?.Faithfulness ?? 3, 1, 5),
                result?.Critique ?? "");
        }
        catch
        {
            return (3, 3, "critique failed");
        }
    }

    private sealed class RetrievalDecisionResponse
    {
        [JsonPropertyName("needsRetrieval")]
        public bool NeedsRetrieval { get; set; }
        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; } = "";
    }

    private sealed class CritiqueResponse
    {
        [JsonPropertyName("relevance")]
        public int Relevance { get; set; }
        [JsonPropertyName("faithfulness")]
        public int Faithfulness { get; set; }
        [JsonPropertyName("critique")]
        public string Critique { get; set; } = "";
    }
}

/// <summary>Result of the Self-RAG flow.</summary>
public sealed class SelfRagResult
{
    /// <summary>The generated answer.</summary>
    public required string Answer { get; init; }

    /// <summary>Whether the system decided retrieval was needed.</summary>
    public bool NeedsRetrieval { get; init; }

    /// <summary>LLM reasoning for the retrieval decision.</summary>
    public string RetrievalReasoning { get; init; } = "";

    /// <summary>Relevance score (1-5).</summary>
    public int Relevance { get; init; }

    /// <summary>Faithfulness score (1-5).</summary>
    public int Faithfulness { get; init; }

    /// <summary>Average of relevance and faithfulness.</summary>
    public double AverageScore { get; init; }

    /// <summary>Self-critique summary.</summary>
    public string Critique { get; init; } = "";

    /// <summary>The retrieved chunks used for generation.</summary>
    public List<string> RetrievedChunks { get; init; } = [];
}
