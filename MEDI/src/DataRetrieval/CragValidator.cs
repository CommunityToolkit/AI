using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataRetrieval;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Corrective RAG (CRAG) validator — scores retrieval quality and routes through
/// one of three paths:
/// <list type="bullet">
///   <item>Correct (≥4): use results as-is</item>
///   <item>Ambiguous (2-3): flag for follow-up, keep results with warning</item>
///   <item>Incorrect (&lt;2): clear chunks, set low_confidence flag</item>
/// </list>
/// </summary>
public class CragValidator : RetrievalResultProcessor
{
    private readonly IChatClient _chatClient;

    /// <summary>Number of top chunks to evaluate.</summary>
    public int EvaluateTopN { get; init; } = 3;

    /// <summary>Maximum preview length per passage (characters).</summary>
    public int PreviewLength { get; init; } = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="CragValidator"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client for LLM inference.</param>
    public CragValidator(IChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <inheritdoc/>
    public override async Task<RetrievalResults> ProcessAsync(
        RetrievalResults results, RetrievalQuery query, CancellationToken cancellationToken = default)
    {
        if (results.Chunks.Count == 0)
        {
            results.Metadata["crag_score"] = 0;
            results.Metadata["crag_path"] = "Incorrect";
            results.Metadata["low_confidence"] = true;
            return results;
        }

        var topChunks = results.Chunks.Take(EvaluateTopN).ToList();
        var passagesText = string.Join("\n---\n",
            topChunks.Select(c => c.Content.Length > PreviewLength
                ? c.Content[..PreviewLength]
                : c.Content));

        var prompt = $$"""
            Rate how well these passages answer the query on a scale of 1-5:
            5 = directly answers with specific details
            4 = highly relevant, mostly answers
            3 = somewhat related but incomplete
            2 = tangentially related
            1 = completely different topic

            Query: {{query.Text}}
            Passages:
            {{passagesText}}

            Return ONLY valid JSON: {"score": 5, "reasoning": "explanation"}
            """;

        var options = new ChatOptions
        {
            MaxOutputTokens = 200,
            ResponseFormat = ChatResponseFormat.Json
        };

        int score;
        try
        {
            var response = await _chatClient.GetResponseAsync(prompt, options, cancellationToken);
            var result = JsonSerializer.Deserialize<CragResponse>(
                response.Text ?? "{}", JsonDefaults.Options);
            score = result?.Score is >= 1 and <= 5 ? result.Score : 3;
            if (result?.Reasoning is not null)
                results.Metadata["crag_reasoning"] = result.Reasoning;
        }
        catch
        {
            score = 3;
        }

        results.Metadata["crag_score"] = score;

        if (score >= 4)
        {
            results.Metadata["crag_path"] = "Correct";
        }
        else if (score >= 2)
        {
            results.Metadata["crag_path"] = "Ambiguous";
            results.Metadata["needs_followup"] = true;
        }
        else
        {
            results.Metadata["crag_path"] = "Incorrect";
            results.Metadata["low_confidence"] = true;
            results.Chunks = [];
        }

        return results;
    }

    private sealed class CragResponse
    {
        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }
}
