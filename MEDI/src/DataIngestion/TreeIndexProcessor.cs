using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommunityToolkit.DataIngestion;

/// <summary>
/// A RAPTOR-style tree index processor that generates summary nodes at multiple levels.
/// </summary>
/// <remarks>
/// For each document's chunks, generates:
/// <list type="bullet">
/// <item>Level 1 (Branch): LLM summary of all chunks within the same document</item>
/// <item>Level 2 (Root): LLM summary of all branch summaries (corpus overview)</item>
/// </list>
/// Summary nodes are injected into the same chunk stream with level metadata.
/// The vector store writer stores them in the same collection as leaf chunks.
/// Standard <c>VectorStoreCollection.SearchAsync</c> naturally returns a mix of leaf
/// chunks (specific detail) and summary nodes (broader context).
/// </remarks>
public sealed class TreeIndexProcessor : IngestionChunkProcessor<string>
{
    private readonly IChatClient _chatClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeIndexProcessor"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client used for summarization.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public TreeIndexProcessor(IChatClient chatClient, ILogger<TreeIndexProcessor>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? NullLogger<TreeIndexProcessor>.Instance;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Collect all chunks, grouping by source document for summarization
        var chunksByDoc = new Dictionary<string, List<IngestionChunk<string>>>();
        var allChunks = new List<IngestionChunk<string>>();

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            // Mark as leaf node
            chunk.Metadata[MetadataKeys.Level] = 0;
            chunk.Metadata[MetadataKeys.ChunkType] = MetadataKeys.ChunkTypeOriginal;
            chunk.Metadata[MetadataKeys.ParentId] = "";

            allChunks.Add(chunk);

            var docId = SanitizeDocId(chunk.Document.Identifier);
            if (!chunksByDoc.TryGetValue(docId, out var list))
                chunksByDoc[docId] = list = [];
            list.Add(chunk);
        }

        // Yield all original leaf chunks first
        foreach (var chunk in allChunks)
            yield return chunk;

        // Level 1 (Branch): Generate document-level summaries
        var branchSummaries = new List<(string DocId, string Summary)>();
        foreach (var (docId, docChunks) in chunksByDoc)
        {
            var combinedText = string.Join("\n\n", docChunks.Select(c => c.Content));
            var summary = await SummarizeAsync(
                $"Summarize the following text in 2-3 concise sentences. Capture key concepts and technologies.\n\n{combinedText}", ct);

            branchSummaries.Add((docId, summary));

            // Update leaf chunks to reference their branch parent
            foreach (var leaf in docChunks)
                leaf.Metadata[MetadataKeys.ParentId] = $"branch-{docId}";

            // Create and yield branch summary chunk
            var branchChunk = new IngestionChunk<string>(summary, docChunks[0].Document, docChunks[0].Context);
            branchChunk.Metadata[MetadataKeys.Level] = 1;
            branchChunk.Metadata[MetadataKeys.ChunkType] = MetadataKeys.ChunkTypeBranchSummary;
            branchChunk.Metadata[MetadataKeys.ParentId] = "root";
            yield return branchChunk;
        }

        // Level 2 (Root): Generate corpus-level summary
        if (branchSummaries.Count > 0)
        {
            var allBranchText = string.Join("\n\n",
                branchSummaries.Select(b => $"[{b.DocId}]: {b.Summary}"));

            var rootSummary = await SummarizeAsync(
                "Write a single 2-3 sentence overview of the entire corpus:\n\n" + allBranchText, ct);

            var rootChunk = new IngestionChunk<string>(rootSummary, allChunks[0].Document, allChunks[0].Context);
            rootChunk.Metadata[MetadataKeys.Level] = 2;
            rootChunk.Metadata[MetadataKeys.ChunkType] = MetadataKeys.ChunkTypeRootSummary;
            rootChunk.Metadata[MetadataKeys.ParentId] = "";
            yield return rootChunk;
        }
    }

    private async Task<string> SummarizeAsync(string prompt, CancellationToken ct)
    {
        var fullPrompt = prompt + "\nReturn ONLY valid JSON: {\"summary\": \"your summary here\"}";

        try
        {
            var response = await _chatClient.GetResponseAsync(fullPrompt,
                new ChatOptions
                {
                    MaxOutputTokens = 200,
                    ResponseFormat = ChatResponseFormat.Json
                }, ct);

            var result = JsonSerializer.Deserialize<SummaryResponse>(
                response.Text ?? "{}", JsonDefaults.Options);
            return result?.Summary ?? "Summary unavailable";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tree index summarization failed; using placeholder.");
            return "Summary unavailable";
        }
    }

    private static string SanitizeDocId(string identifier) =>
        Path.GetFileNameWithoutExtension(identifier).Replace(" ", "-").ToLowerInvariant();

    private sealed class SummaryResponse
    {
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";
    }
}
