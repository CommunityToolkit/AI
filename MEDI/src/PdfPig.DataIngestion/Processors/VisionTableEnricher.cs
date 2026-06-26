using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.Processors;

/// <summary>
/// Enriches table elements in a document by sending their content
/// to a vision-capable LLM via <see cref="IChatClient"/> to extract markdown table content.
/// </summary>
public sealed class VisionTableEnricher : IngestionDocumentProcessor
{
    private readonly IChatClient chatClient;

    /// <summary>
    /// Creates a new <see cref="VisionTableEnricher"/>.
    /// </summary>
    /// <param name="chatClient">The chat client used to interact with a vision-capable LLM.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public VisionTableEnricher(IChatClient chatClient)
    {
        this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ProcessAsync(
        IngestionDocument document, CancellationToken cancellationToken = default)
    {
        foreach (var element in document.EnumerateContent())
        {
            if (element is not IngestionDocumentTable table)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Try to get page image for vision-based extraction
            byte[]? imageBytes = null;
            if (element.PageNumber is int pageNumber)
            {
                foreach (var section in document.Sections)
                {
                    if (section.PageNumber == pageNumber &&
                        section.HasMetadata &&
                        section.Metadata.TryGetValue("page_image", out var imageObj))
                    {
                        imageBytes = imageObj as byte[];
                        break;
                    }
                }
            }

            ChatMessage[] messages;
            if (imageBytes is not null)
            {
                // Vision approach: send actual page image
                messages = new[]
                {
                    new ChatMessage(ChatRole.System,
                        "You are a table extraction engine. Extract the table from the image as a markdown table. " +
                        "Use | as column separators. Include a header separator (| --- | --- |). " +
                        "Output ONLY the markdown table, nothing else."),
                    new ChatMessage(ChatRole.User, (IList<AIContent>)new AIContent[]
                    {
                        new DataContent(imageBytes, "image/png"),
                        new TextContent("Extract the table from this image as a markdown table.")
                    })
                };
            }
            else
            {
                // Fallback: text-based extraction
                messages = new[]
                {
                    new ChatMessage(ChatRole.User,
                        "Extract the following table content into a well-formatted markdown table. " +
                        "Only output the markdown table, no other text.\n\n" +
                        (table.Text ?? string.Empty))
                };
            }

            var response = await chatClient.GetResponseAsync(
                messages,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            table.Metadata["enriched_markdown_table"] = response.Text;
        }

        return document;
    }
}
