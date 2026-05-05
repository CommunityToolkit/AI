using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataRetrieval;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Generates multiple query variants via LLM to improve recall.
/// The pipeline's RRF (Reciprocal Rank Fusion) merge deduplicates results from all variants.
/// </summary>
public class MultiQueryExpander : RetrievalQueryProcessor
{
    private readonly IChatClient _chatClient;

    /// <summary>Number of additional query variants to generate.</summary>
    public int VariantCount { get; init; } = 3;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiQueryExpander"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client for LLM inference.</param>
    public MultiQueryExpander(IChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <inheritdoc/>
    public override async Task<RetrievalQuery> ProcessAsync(
        RetrievalQuery query, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            Given this search query, generate exactly {{VariantCount}} alternative phrasings that capture
            different angles or terminology.

            Query: {{query.Text}}

            Return ONLY valid JSON: {"variants": ["query1", "query2", "query3"]}
            """;

        var options = new ChatOptions
        {
            MaxOutputTokens = 200,
            ResponseFormat = ChatResponseFormat.Json
        };

        List<string> variants;
        try
        {
            var response = await _chatClient.GetResponseAsync(prompt, options, cancellationToken);
            var result = JsonSerializer.Deserialize<QueryExpansionResponse>(
                response.Text ?? "{}", JsonDefaults.Options);

            variants = (result?.Variants ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 10)
                .Take(VariantCount)
                .ToList();
        }
        catch
        {
            variants = [];
        }

        var allVariants = new List<string> { query.Text };
        allVariants.AddRange(variants);
        query.Variants = allVariants;
        return query;
    }

    private sealed class QueryExpansionResponse
    {
        [JsonPropertyName("variants")]
        public string[] Variants { get; set; } = [];
    }
}
