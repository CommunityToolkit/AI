using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommunityToolkit.DataIngestion;

/// <summary>
/// An <see cref="IngestionChunkProcessor{T}"/> that generates hypothetical questions each chunk
/// could answer, storing them as additional retrieval vectors in the same collection.
/// This is "reverse HyDE" — bridging the query-document gap at ingestion time.
/// </summary>
/// <remarks>
/// For each original chunk, yields:
/// <list type="number">
/// <item>The original chunk (with <c>chunk_type=original</c> metadata)</item>
/// <item>N question chunks (with <c>chunk_type=hypothetical_query</c> and <c>parent_chunk_id</c>)</item>
/// </list>
/// Question chunks contain the question text as their Content — the embedding model creates
/// vectors in "question space," directly matching user queries.
/// </remarks>
public sealed class HypotheticalQueryProcessor : IngestionChunkProcessor<string>
{
    private readonly IChatClient _chatClient;
    private readonly int _questionsPerChunk;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HypotheticalQueryProcessor"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client used for question generation.</param>
    /// <param name="questionsPerChunk">Number of hypothetical questions to generate per chunk (default: 3).</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public HypotheticalQueryProcessor(IChatClient chatClient, int questionsPerChunk = 3, ILogger<HypotheticalQueryProcessor>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _questionsPerChunk = questionsPerChunk;
        _logger = logger ?? NullLogger<HypotheticalQueryProcessor>.Instance;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            var chunkId = chunk.Content.GetHashCode().ToString();

            // Yield original chunk first (with consistent metadata keys)
            chunk.Metadata[MetadataKeys.ChunkType] = MetadataKeys.ChunkTypeOriginal;
            chunk.Metadata[MetadataKeys.ParentChunkId] = "";
            chunk.Metadata[MetadataKeys.HypotheticalQuestions] = "";
            yield return chunk;

            // Generate hypothetical questions (best-effort)
            var questionChunks = new List<IngestionChunk<string>>();
            try
            {
                var questions = await GenerateQuestionsAsync(chunk.Content, ct);
                chunk.Metadata[MetadataKeys.HypotheticalQuestions] = string.Join(" | ", questions);

                foreach (var question in questions)
                {
                    var qChunk = new IngestionChunk<string>(question, chunk.Document, chunk.Context);
                    qChunk.Metadata[MetadataKeys.ChunkType] = MetadataKeys.ChunkTypeHypotheticalQuery;
                    qChunk.Metadata[MetadataKeys.ParentChunkId] = chunkId;
                    qChunk.Metadata[MetadataKeys.HypotheticalQuestions] = "";
                    questionChunks.Add(qChunk);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hypothetical question generation failed for chunk; continuing with original only.");
            }

            foreach (var qChunk in questionChunks)
                yield return qChunk;
        }
    }

    private async Task<List<string>> GenerateQuestionsAsync(string chunkContent, CancellationToken ct)
    {
        var prompt = $"Generate exactly {_questionsPerChunk} questions that the following "
            + "text passage answers.\n"
            + "Return ONLY valid JSON matching: {\"questions\": [\"question1?\", \"question2?\", \"question3?\"]}"
            + $"\n\nPassage:\n{chunkContent}";

        var response = await _chatClient.GetResponseAsync(prompt,
            new ChatOptions
            {
                MaxOutputTokens = 200,
                ResponseFormat = ChatResponseFormat.Json
            }, ct);

        var result = JsonSerializer.Deserialize<QuestionsResponse>(
            response.Text ?? "{}", JsonDefaults.Options);

        return (result?.Questions ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q) && q.Length > 10)
            .Take(_questionsPerChunk)
            .ToList();
    }

    private sealed class QuestionsResponse
    {
        [JsonPropertyName("questions")]
        public List<string> Questions { get; set; } = [];
    }
}
