using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.Processors;

/// <summary>
/// Enriches document elements that have minimal or no text content by performing
/// vision LLM-based OCR, such as for scanned pages or image-heavy regions.
/// </summary>
public sealed class VisionOcrEnricher : IngestionDocumentProcessor
{
    private readonly IChatClient chatClient;

    /// <summary>
    /// Creates a new <see cref="VisionOcrEnricher"/>.
    /// </summary>
    /// <param name="chatClient">The chat client used to interact with a vision-capable LLM.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public VisionOcrEnricher(IChatClient chatClient)
    {
        this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ProcessAsync(
        IngestionDocument document, CancellationToken cancellationToken = default)
    {
        // Iterate by index so we can replace elements in-place.
        // This is necessary because the MEDI chunker uses GetMarkdown() (which returns
        // the immutable constructor parameter) rather than element.Text. Setting Text alone
        // would leave stale placeholder content in GetMarkdown(), causing the chunker to
        // produce chunks with placeholder text instead of the OCR'd content.
        foreach (var section in document.Sections)
        {
            for (int i = 0; i < section.Elements.Count; i++)
            {
                var element = section.Elements[i];
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(element.Text))
                {
                    continue;
                }

                // Try to get page image for vision-based OCR
                byte[]? imageBytes = null;
                if (element.PageNumber is int pageNumber)
                {
                    foreach (var s in document.Sections)
                    {
                        if (s.PageNumber == pageNumber &&
                            s.HasMetadata &&
                            s.Metadata.TryGetValue("page_image", out var imageObj))
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
                            "You are a precise OCR engine. Extract all visible text from the provided image exactly as it appears. " +
                            "Preserve line breaks and formatting. Output only the extracted text, no commentary."),
                        new ChatMessage(ChatRole.User, (IList<AIContent>)new AIContent[]
                        {
                            new DataContent(imageBytes, "image/png"),
                            new TextContent("Extract all text from this image.")
                        })
                    };
                }
                else
                {
                    // Fallback: text-based approach when no image available
                    messages = new[]
                    {
                        new ChatMessage(ChatRole.User,
                            "You are an OCR engine. Extract all visible text from the following content. " +
                            "Return only the extracted text, preserving the original layout as much as possible.\n\n" +
                            (element.Text ?? string.Empty))
                    };
                }

                var response = await chatClient.GetResponseAsync(
                    messages,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    // Replace the element with a new one whose markdown (constructor param)
                    // contains the OCR text, so downstream chunkers see it via GetMarkdown().
                    var replacement = new IngestionDocumentParagraph(response.Text)
                    {
                        Text = response.Text,
                        PageNumber = element.PageNumber
                    };

                    if (element.HasMetadata)
                    {
                        foreach (var kv in element.Metadata)
                        {
                            replacement.Metadata[kv.Key] = kv.Value;
                        }
                    }

                    replacement.Metadata["ocr_source"] = "vision_llm";
                    section.Elements[i] = replacement;
                }
            }
        }

        return document;
    }
}
