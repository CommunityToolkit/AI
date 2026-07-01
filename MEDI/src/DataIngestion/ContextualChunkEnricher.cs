using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;

namespace CommunityToolkit.DataIngestion;

/// <summary>
/// An <see cref="IngestionChunkProcessor{T}"/> that enriches text chunks with contextual
/// summaries for improved RAG retrieval.
/// </summary>
public sealed class ContextualChunkEnricher : IngestionChunkProcessor<string>
{
    private readonly IChatClient _chatClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextualChunkEnricher"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client used to generate contextual summaries.</param>
    public ContextualChunkEnricher(IChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            var prompt = GetPromptForChunk(chunk);
            var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);

            chunk.Metadata[MetadataKeys.ContextualSummary] = response.Text;
            yield return chunk;
        }
    }

    private static string GetPromptForChunk(IngestionChunk<string> chunk)
    {
        var elementType = chunk.Metadata.TryGetValue("element_type", out var t) ? t as string : null;
        var instruction = elementType switch
        {
            "table" => "Summarize this table for search retrieval. Describe what data it contains, including key metrics, column headers, and notable values. Output only the summary sentence, nothing else.",
            "picture" or "caption" => "Summarize what this figure or image refers to for search retrieval. Output only the summary sentence, nothing else.",
            _ => "Provide a single concise sentence summarizing the following text for use in search retrieval. Output only the summary sentence, nothing else."
        };

        return instruction + "\n\n" + chunk.Content;
    }
}
