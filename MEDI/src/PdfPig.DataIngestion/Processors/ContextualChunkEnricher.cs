using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.Processors;

/// <summary>
/// Enriches text chunks with contextual summaries for improved RAG retrieval.
/// Uses <see cref="IChatClient"/> to generate a brief summary of each chunk.
/// </summary>
public sealed class ContextualChunkEnricher : IngestionChunkProcessor<string>
{
    private readonly IChatClient chatClient;

    /// <summary>
    /// Creates a new <see cref="ContextualChunkEnricher"/>.
    /// </summary>
    /// <param name="chatClient">The chat client used to generate contextual summaries.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public ContextualChunkEnricher(IChatClient chatClient)
    {
        this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var prompt = GetPromptForChunk(chunk);

            var messages = new[]
            {
                new ChatMessage(ChatRole.User, prompt)
            };

            var response = await chatClient.GetResponseAsync(
                messages,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            chunk.Metadata["contextual_summary"] = response.Text;

            yield return chunk;
        }
    }

    private static string GetPromptForChunk(IngestionChunk<string> chunk)
    {
        var elementType = chunk.Metadata.TryGetValue("element_type", out var t) ? t as string : null;

        var instruction = elementType switch
        {
            "table" =>
                "Summarize this table for search retrieval. Describe what data it contains, " +
                "including key metrics, column headers, and notable values. " +
                "Output only the summary sentence, nothing else.",
            "picture" or "caption" =>
                "Summarize what this figure or image refers to for search retrieval. " +
                "Output only the summary sentence, nothing else.",
            _ =>
                "Provide a single concise sentence summarizing the following text for use " +
                "in search retrieval. Output only the summary sentence, nothing else."
        };

        return instruction + "\n\n" + chunk.Content;
    }
}
