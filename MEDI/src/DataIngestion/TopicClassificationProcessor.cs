using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommunityToolkit.DataIngestion;

/// <summary>
/// An <see cref="IngestionChunkProcessor{T}"/> that classifies each chunk into primary and
/// optional secondary topics from a configurable taxonomy. Stores <c>topic_primary</c>
/// (single label) and <c>topic_secondary</c> (comma-separated) as chunk metadata.
/// </summary>
public sealed class TopicClassificationProcessor : IngestionChunkProcessor<string>
{
    private readonly IChatClient _chatClient;
    private readonly string[] _taxonomy;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopicClassificationProcessor"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client used for topic classification.</param>
    /// <param name="taxonomy">Valid topic labels (e.g., "web", "data", "security", "performance", "architecture").</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public TopicClassificationProcessor(IChatClient chatClient, string[] taxonomy, ILogger<TopicClassificationProcessor>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
        _logger = logger ?? NullLogger<TopicClassificationProcessor>.Instance;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            string primary = "unknown";
            string secondary = "";

            try
            {
                var prompt = BuildPrompt(chunk.Content);
                var options = new ChatOptions
                {
                    MaxOutputTokens = 100,
                    ResponseFormat = ChatResponseFormat.Json
                };

                var response = await _chatClient.GetResponseAsync(prompt, options, ct);
                var parsed = JsonSerializer.Deserialize<TopicResponse>(
                    response.Text ?? "{}", JsonDefaults.Options)
                    ?? new TopicResponse();

                if (_taxonomy.Contains(parsed.Primary))
                {
                    primary = parsed.Primary;
                    var validSecondary = (parsed.Secondary ?? [])
                        .Where(t => _taxonomy.Contains(t) && t != primary)
                        .Distinct();
                    secondary = string.Join(",", validSecondary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Topic classification failed for chunk; defaulting to 'unknown'.");
            }

            chunk.Metadata[MetadataKeys.TopicPrimary] = primary;
            chunk.Metadata[MetadataKeys.TopicSecondary] = secondary;

            yield return chunk;
        }
    }

    private string BuildPrompt(string text) => $$"""
        Classify this text into topics from: [{{string.Join(", ", _taxonomy.Select(t => $"\"{t}\""))}}].

        Return ONLY valid JSON (no extra text):
        {"primary": "topic", "secondary": ["topic2", "topic3"]}

        If the text fits only one topic, return an empty secondary array.

        Text: {{text}}
        """;

    private sealed class TopicResponse
    {
        [JsonPropertyName("primary")]
        public string Primary { get; set; } = "";

        [JsonPropertyName("secondary")]
        public List<string> Secondary { get; set; } = [];
    }
}
