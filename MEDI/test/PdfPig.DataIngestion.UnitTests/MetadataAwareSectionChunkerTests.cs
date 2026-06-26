#if NET8_0_OR_GREATER
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;
using CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;
using Xunit;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

public class MetadataAwareSectionChunkerTests
{
    private static MetadataAwareSectionChunker CreateChunker()
    {
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        var options = new IngestionChunkerOptions(tokenizer) { MaxTokensPerChunk = 10000 };
        return new MetadataAwareSectionChunker(options);
    }

    private static async Task<List<IngestionChunk<string>>> CollectChunksAsync(
        MetadataAwareSectionChunker chunker,
        IngestionDocument doc)
    {
        var chunks = new List<IngestionChunk<string>>();
        await foreach (var chunk in chunker.ProcessAsync(doc))
        {
            chunks.Add(chunk);
        }
        return chunks;
    }

    [Fact]
    public async Task ProcessAsync_PropagatesElementTypeMetadata()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        var paragraph = new IngestionDocumentParagraph("Some text") { Text = "Some text" };
        paragraph.Metadata["element_type"] = "text";
        section.Elements.Add(paragraph);
        doc.Sections.Add(section);

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Single(chunks);
        Assert.True(chunks[0].Metadata.ContainsKey("element_type"));
        Assert.Equal("text", chunks[0].Metadata["element_type"]);
    }

    [Fact]
    public async Task ProcessAsync_SkipsBoundingBoxMetadata()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        var paragraph = new IngestionDocumentParagraph("Heading text") { Text = "Heading text" };
        paragraph.Metadata["BoundingBox.Left"] = 10.0;
        paragraph.Metadata["element_type"] = "heading";
        section.Elements.Add(paragraph);
        doc.Sections.Add(section);

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Single(chunks);
        Assert.True(chunks[0].Metadata.ContainsKey("element_type"));
        Assert.Equal("heading", chunks[0].Metadata["element_type"]);
        Assert.False(chunks[0].Metadata.ContainsKey("BoundingBox.Left"));
    }

    [Fact]
    public async Task ProcessAsync_FirstMatchWinsForConflictingKeys()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };

        var paragraph1 = new IngestionDocumentParagraph("First paragraph") { Text = "First paragraph" };
        paragraph1.Metadata["element_type"] = "text";
        section.Elements.Add(paragraph1);

        var paragraph2 = new IngestionDocumentParagraph("Second paragraph") { Text = "Second paragraph" };
        paragraph2.Metadata["element_type"] = "heading";
        section.Elements.Add(paragraph2);

        doc.Sections.Add(section);

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Single(chunks);
        Assert.Equal("text", chunks[0].Metadata["element_type"]);
    }

    [Fact]
    public async Task ProcessAsync_NullMetadataValuesSkipped()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        var paragraph = new IngestionDocumentParagraph("Null meta text") { Text = "Null meta text" };
        paragraph.Metadata["element_type"] = null;
        section.Elements.Add(paragraph);
        doc.Sections.Add(section);

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Single(chunks);
        Assert.False(chunks[0].Metadata.ContainsKey("element_type"));
    }

    [Fact]
    public async Task ProcessAsync_ElementWithoutMetadata_Skipped()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        var paragraph = new IngestionDocumentParagraph("Plain text") { Text = "Plain text" };
        section.Elements.Add(paragraph);
        doc.Sections.Add(section);

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Single(chunks);
        Assert.Empty(chunks[0].Metadata);
    }

    [Fact]
    public async Task ProcessAsync_EmptyTextElement_Skipped()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };

        var emptyParagraph = new IngestionDocumentParagraph("   ") { Text = "" };
        emptyParagraph.Metadata["element_type"] = "empty";
        section.Elements.Add(emptyParagraph);

        var visibleParagraph = new IngestionDocumentParagraph("Visible text") { Text = "Visible text" };
        section.Elements.Add(visibleParagraph);

        doc.Sections.Add(section);

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Single(chunks);
        Assert.False(chunks[0].Metadata.ContainsKey("element_type"));
    }

    [Fact]
    public async Task ProcessAsync_SubstringMatchIsCaseSensitive()
    {
        var doc = new IngestionDocument("test.pdf");
        var section = new IngestionDocumentSection { PageNumber = 1 };
        var paragraph = new IngestionDocumentParagraph("Hello World") { Text = "Hello World" };
        paragraph.Metadata["element_type"] = "greeting";
        section.Elements.Add(paragraph);
        doc.Sections.Add(section);

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Single(chunks);
        Assert.Contains("Hello World", chunks[0].Content);
        Assert.True(chunks[0].Metadata.ContainsKey("element_type"));
        Assert.Equal("greeting", chunks[0].Metadata["element_type"]);
    }

    [Fact]
    public async Task ProcessAsync_EmptyDocument_ReturnsNoChunks()
    {
        var doc = new IngestionDocument("test.pdf");

        var chunker = CreateChunker();
        var chunks = await CollectChunksAsync(chunker, doc);

        Assert.Empty(chunks);
    }
}
#endif
