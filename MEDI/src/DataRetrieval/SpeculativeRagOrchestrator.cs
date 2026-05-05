using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Speculative RAG orchestrator — parallel drafting with a small model + verification by a large model.
/// Leverages .NET's <see cref="Task.WhenAll(Task[])"/> for true concurrent inference.
/// </summary>
/// <remarks>
/// Uses keyed DI: register a "drafter" (small/fast model) and "verifier" (large/accurate model).
/// .NET's async/await + Task.WhenAll enables genuine concurrent LLM calls without GIL limitations.
/// </remarks>
public class SpeculativeRagOrchestrator
{
    private readonly IChatClient _drafterClient;
    private readonly IChatClient _verifierClient;
    private readonly ILogger? _logger;

    /// <summary>Number of parallel drafts to generate.</summary>
    public int DraftCount { get; init; } = 3;

    /// <summary>Maximum tokens for each draft response.</summary>
    public int DraftMaxTokens { get; init; } = 500;

    /// <summary>Maximum tokens for the verification response.</summary>
    public int VerifyMaxTokens { get; init; } = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpeculativeRagOrchestrator"/> class.
    /// </summary>
    /// <param name="drafterClient">Small/fast model for parallel drafting.</param>
    /// <param name="verifierClient">Large/accurate model for verification.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public SpeculativeRagOrchestrator(
        IChatClient drafterClient,
        IChatClient verifierClient,
        ILoggerFactory? loggerFactory = null)
    {
        _drafterClient = drafterClient ?? throw new ArgumentNullException(nameof(drafterClient));
        _verifierClient = verifierClient ?? throw new ArgumentNullException(nameof(verifierClient));
        _logger = loggerFactory?.CreateLogger<SpeculativeRagOrchestrator>();
    }

    /// <summary>
    /// Runs the Speculative RAG flow: retrieve → parallel draft → verify → select best.
    /// </summary>
    public async Task<SpeculativeRagResult> GenerateAsync<TKey, TRecord>(
        VectorStoreCollection<TKey, TRecord> collection,
        string query,
        Func<TRecord, string> contentSelector,
        int topK = 5,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        where TRecord : class
    {
        var retrievedChunks = new List<string>();
        await foreach (var hit in collection.SearchAsync(query, top: topK, cancellationToken: cancellationToken))
        {
            if (hit.Record is not null)
                retrievedChunks.Add(contentSelector(hit.Record));
        }

        _logger?.LogDebug("Speculative RAG: retrieved {Count} chunks", retrievedChunks.Count);

        var subsets = CreateChunkSubsets(retrievedChunks, DraftCount);

        var draftTasks = subsets.Select(async (subset, idx) =>
        {
            var prompt = $"Based on these passages, answer the question briefly:\n\n" +
                string.Join("\n---\n", subset) +
                $"\n\nQuestion: {query}";

            var options = new ChatOptions { MaxOutputTokens = DraftMaxTokens };
            var response = await _drafterClient.GetResponseAsync(prompt, options, cancellationToken);
            return new Draft
            {
                Index = idx + 1,
                Text = (response.Text ?? "").Trim(),
                ChunkSubset = subset
            };
        }).ToArray();

        var startTime = Environment.TickCount64;
        var drafts = await Task.WhenAll(draftTasks);
        var parallelMs = Environment.TickCount64 - startTime;

        _logger?.LogInformation(
            "Speculative RAG: {Count} drafts generated in {Ms}ms (parallel)",
            drafts.Length, parallelMs);

        var verification = await VerifyDraftsAsync(query, drafts, cancellationToken);

        var bestDraft = drafts.FirstOrDefault(d => d.Index == verification.BestDraftIndex)
            ?? drafts.First();

        return new SpeculativeRagResult
        {
            Answer = bestDraft.Text,
            BestDraftIndex = verification.BestDraftIndex,
            Confidence = verification.Confidence,
            VerificationReasoning = verification.Reasoning,
            Drafts = drafts.Select(d => new DraftResult
            {
                Index = d.Index,
                Text = d.Text
            }).ToList(),
            ParallelDraftMs = parallelMs,
            RetrievedChunks = retrievedChunks
        };
    }

    private static List<string[]> CreateChunkSubsets(List<string> chunks, int subsetCount)
    {
        if (chunks.Count == 0)
            return Enumerable.Range(0, subsetCount).Select(_ => Array.Empty<string>()).ToList();

        var subsets = new List<string[]>();
        var chunkCount = chunks.Count;

        for (int i = 0; i < subsetCount; i++)
        {
            var start = (int)Math.Round((double)i * chunkCount / subsetCount);
            var end = Math.Min(start + Math.Max(2, chunkCount / subsetCount + 1), chunkCount);
            subsets.Add(chunks.Skip(start).Take(end - start).ToArray());
        }

        return subsets;
    }

    private async Task<VerificationResponse> VerifyDraftsAsync(
        string query, Draft[] drafts, CancellationToken ct)
    {
        var draftSummaries = drafts.Select(d =>
        {
            var text = d.Text.Length > 300 ? d.Text[..300] + "..." : d.Text;
            return $"Draft {d.Index}: {text}";
        });

        var prompt = $"Question: \"{query}\"\n\n" +
            string.Join("\n\n", draftSummaries) +
            "\n\nEvaluate which draft is most accurate and complete. " +
            "Return ONLY valid JSON: {\"bestDraftIndex\": 1, \"confidence\": 0.85, \"reasoning\": \"one sentence\"}";

        try
        {
            var response = await _verifierClient.GetResponseAsync(prompt,
                new ChatOptions
                {
                    MaxOutputTokens = VerifyMaxTokens,
                    ResponseFormat = ChatResponseFormat.Json
                }, ct);

            return JsonSerializer.Deserialize<VerificationResponse>(
                response.Text ?? "{}", JsonDefaults.Options) ?? new VerificationResponse();
        }
        catch
        {
            return new VerificationResponse { BestDraftIndex = 1, Confidence = 0.5, Reasoning = "verification failed" };
        }
    }

    private sealed class Draft
    {
        public int Index { get; init; }
        public string Text { get; init; } = "";
        public string[] ChunkSubset { get; init; } = [];
    }

    private sealed class VerificationResponse
    {
        [JsonPropertyName("bestDraftIndex")]
        public int BestDraftIndex { get; set; } = 1;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; } = "";
    }
}

/// <summary>Result of the Speculative RAG flow.</summary>
public sealed class SpeculativeRagResult
{
    /// <summary>The selected best answer.</summary>
    public required string Answer { get; init; }

    /// <summary>Index (1-based) of the selected draft.</summary>
    public int BestDraftIndex { get; init; }

    /// <summary>Verifier confidence (0-1).</summary>
    public double Confidence { get; init; }

    /// <summary>Verifier reasoning.</summary>
    public string VerificationReasoning { get; init; } = "";

    /// <summary>All generated drafts.</summary>
    public List<DraftResult> Drafts { get; init; } = [];

    /// <summary>Time (ms) for parallel draft generation.</summary>
    public long ParallelDraftMs { get; init; }

    /// <summary>Retrieved chunks used for drafting.</summary>
    public List<string> RetrievedChunks { get; init; } = [];
}

/// <summary>Individual draft from the Speculative RAG drafting phase.</summary>
public sealed class DraftResult
{
    /// <summary>Draft index (1-based).</summary>
    public int Index { get; init; }

    /// <summary>Draft text.</summary>
    public string Text { get; init; } = "";
}
