using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataRetrieval;

namespace CommunityToolkit.DataRetrieval;

/// <summary>
/// Adaptive RAG router — classifies queries and routes to the appropriate search paradigm.
/// </summary>
/// <remarks>
/// Routes between:
/// <list type="bullet">
///   <item>Vector: specific factual queries → standard vector search</item>
///   <item>TreeTraversal: broad/thematic queries → top-down tree traversal</item>
///   <item>EntityFiltered: entity-specific queries → vector search with entity metadata filter</item>
/// </list>
/// </remarks>
public class AdaptiveRouter : RetrievalQueryProcessor
{
    private readonly IChatClient _chatClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdaptiveRouter"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client for LLM inference.</param>
    public AdaptiveRouter(IChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <inheritdoc/>
    public override async Task<RetrievalQuery> ProcessAsync(
        RetrievalQuery query, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            Classify this search query into one of these categories:
            - "vector": specific factual question about a particular topic, API, or concept
            - "tree": broad/thematic question requiring multi-document overview or summary
            - "entity": question about a specific named entity (person, organization, technology)

            Query: "{{query.Text}}"

            Return ONLY valid JSON: {"paradigm": "vector", "reasoning": "one sentence"}
            """;

        string paradigm;
        try
        {
            var response = await _chatClient.GetResponseAsync(prompt,
                new ChatOptions
                {
                    MaxOutputTokens = 100,
                    ResponseFormat = ChatResponseFormat.Json
                }, cancellationToken);

            var result = JsonSerializer.Deserialize<RouterResponse>(
                response.Text ?? "{}", JsonDefaults.Options);

            paradigm = result?.Paradigm switch
            {
                "vector" or "tree" or "entity" => result.Paradigm,
                _ => "vector"
            };

            if (result?.Reasoning is not null)
                query.Metadata["router_reasoning"] = result.Reasoning;
        }
        catch
        {
            paradigm = "vector";
        }

        query.Metadata["search_paradigm"] = paradigm;
        return query;
    }

    private sealed class RouterResponse
    {
        [JsonPropertyName("paradigm")]
        public string Paradigm { get; set; } = "vector";

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }
}
