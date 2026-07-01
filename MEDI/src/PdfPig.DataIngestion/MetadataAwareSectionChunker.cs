using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>
/// A chunker that wraps <see cref="SectionChunker"/> and propagates element metadata
/// (such as "element_type") to the resulting chunks. This works around the fact that
/// the built-in MEDI chunkers do not copy element metadata to chunks.
/// </summary>
/// <remarks>
/// After the inner chunker produces chunks, this class matches each chunk's content
/// back to its source elements via substring containment and copies metadata from
/// the matching elements to the chunk. For keys with conflicting values across
/// multiple matching elements, the value from the first match wins.
/// </remarks>
internal sealed class MetadataAwareSectionChunker : IngestionChunker<string>
{
    private readonly SectionChunker _inner;

    /// <summary>
    /// Creates a new <see cref="MetadataAwareSectionChunker"/>.
    /// </summary>
    /// <param name="options">The chunker options (tokenizer, max tokens, overlap).</param>
    public MetadataAwareSectionChunker(IngestionChunkerOptions options)
    {
        _inner = new SectionChunker(options);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IngestionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var elementIndex = BuildElementIndex(document);

        await foreach (var chunk in _inner.ProcessAsync(document, cancellationToken).ConfigureAwait(false))
        {
            ResolveMetadata(chunk, elementIndex);
            yield return chunk;
        }
    }

    /// <summary>
    /// Builds an index of element text snippets to their metadata.
    /// Only includes elements that actually have metadata set.
    /// </summary>
    private static List<(string Text, IDictionary<string, object?> Metadata)> BuildElementIndex(
        IngestionDocument document)
    {
        var index = new List<(string Text, IDictionary<string, object?> Metadata)>();

        foreach (var section in document.Sections)
        {
            foreach (var element in section.Elements)
            {
                if (!element.HasMetadata)
                {
                    continue;
                }

                // Use the element's text content for matching.
                // SectionChunker concatenates element GetMarkdown() values with newlines,
                // so we match against GetMarkdown() for reliable substring containment.
                var text = element.GetMarkdown();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                index.Add((text, element.Metadata));
            }
        }

        return index;
    }

    /// <summary>
    /// Matches a chunk's content back to source elements and copies their metadata.
    /// For keys that appear in multiple matching elements, the first match's value wins.
    /// </summary>
    private static void ResolveMetadata(
        IngestionChunk<string> chunk,
        List<(string Text, IDictionary<string, object?> Metadata)> elementIndex)
    {
        var content = chunk.Content;
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        foreach (var (text, metadata) in elementIndex)
        {
            if (!content.Contains(text, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var kvp in metadata)
            {
                // Skip bounding box metadata — it's per-element, not meaningful at chunk level
                if (kvp.Key.StartsWith("BoundingBox.", StringComparison.Ordinal))
                {
                    continue;
                }

                // First match wins for each key
                if (kvp.Value is not null && !chunk.Metadata.ContainsKey(kvp.Key))
                {
                    chunk.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }
    }
}
