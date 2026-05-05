using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommunityToolkit.DataIngestion;

/// <summary>
/// An <see cref="IngestionChunkProcessor{T}"/> that uses an LLM to extract named entities
/// (people, organizations, technologies, versions) from each chunk and stores them as
/// metadata for downstream filtered vector search.
/// </summary>
public sealed class EntityExtractionProcessor : IngestionChunkProcessor<string>
{
    private readonly IChatClient _chatClient;
    private readonly ILogger _logger;

    private static readonly string[] EntityKeys =
        [MetadataKeys.EntitiesPeople, MetadataKeys.EntitiesOrganizations, MetadataKeys.EntitiesTechnologies, MetadataKeys.EntitiesVersions];

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityExtractionProcessor"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client used for entity extraction.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public EntityExtractionProcessor(IChatClient chatClient, ILogger<EntityExtractionProcessor>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? NullLogger<EntityExtractionProcessor>.Instance;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            try
            {
                var prompt = $$"""
                    Extract named entities from the following text.
                    Return ONLY valid JSON matching: {"people": ["name"], "organizations": ["org"], "technologies": ["tech"], "versions": ["v1"]}
                    Use empty arrays [] when none found for a category.

                    Text:
                    {{chunk.Content}}
                    """;

                var options = new ChatOptions
                {
                    MaxOutputTokens = 300,
                    ResponseFormat = ChatResponseFormat.Json
                };

                var response = await _chatClient.GetResponseAsync(prompt, options, ct);
                var entities = JsonSerializer.Deserialize<EntityResponse>(
                    response.Text ?? "{}", JsonDefaults.Options);

                if (entities is not null)
                {
                    chunk.Metadata[MetadataKeys.EntitiesPeople] = string.Join(", ", entities.People ?? []);
                    chunk.Metadata[MetadataKeys.EntitiesOrganizations] = string.Join(", ", entities.Organizations ?? []);
                    chunk.Metadata[MetadataKeys.EntitiesTechnologies] = string.Join(", ", entities.Technologies ?? []);
                    chunk.Metadata[MetadataKeys.EntitiesVersions] = string.Join(", ", entities.Versions ?? []);
                }
                else
                {
                    SetEmptyEntities(chunk);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Entity extraction failed for chunk; using empty defaults.");
                SetEmptyEntities(chunk);
            }

            yield return chunk;
        }
    }

    private static void SetEmptyEntities(IngestionChunk<string> chunk)
    {
        foreach (var key in EntityKeys)
            chunk.Metadata[key] = "";
    }

    private sealed class EntityResponse
    {
        [JsonPropertyName("people")]
        public string[]? People { get; set; }

        [JsonPropertyName("organizations")]
        public string[]? Organizations { get; set; }

        [JsonPropertyName("technologies")]
        public string[]? Technologies { get; set; }

        [JsonPropertyName("versions")]
        public string[]? Versions { get; set; }
    }
}
