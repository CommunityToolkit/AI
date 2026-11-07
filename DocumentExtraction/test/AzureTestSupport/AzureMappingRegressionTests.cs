using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
#if CONTENT_UNDERSTANDING
using NativeResult = Azure.AI.ContentUnderstanding.AnalysisResult;
#else
using NativeResult = Azure.AI.DocumentIntelligence.AnalyzeResult;
#endif

namespace CommunityToolkit.DocumentExtraction.Tests;

#if CONTENT_UNDERSTANDING
public sealed partial class ContentUnderstandingExtractionClientTests
#else
public sealed partial class DocumentIntelligenceExtractionClientTests
#endif
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_TwoPages_KeepsReadingOrder(bool nested)
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["tables"] = new JsonArray();
        content["figures"] = new JsonArray();
        (string Text, string? Role, int Page, int Offset)[] paragraphs =
        [
            ("Title", "title", 1, 0),
            ("Body one", null, 1, 10),
            ("Page 1 of 2", "pageFooter", 1, 25),
            ("Header two", "pageHeader", 2, 40),
            ("Body two", null, 2, 55),
        ];
        char[] text = new string(' ', 80).ToCharArray();
        var nodes = new JsonArray();
        foreach (var (value, role, page, offset) in paragraphs)
        {
            value.CopyTo(0, text, offset, value.Length);
            var paragraph = new JsonObject { ["content"] = value };
            if (role is not null)
            {
                paragraph["role"] = role;
            }

            SetSpan(paragraph, offset, value.Length);
            SetSource(paragraph, page, AzureWireFixture.Coordinates);
            nodes.Add(paragraph);
        }

        content["paragraphs"] = nodes;
        content[AzureWireFixture.IsContentUnderstanding ? "markdown" : "content"] = new string(text);
        var pages = content["pages"]!.AsArray();
        var second = pages[0]!.DeepClone();
        pages[0]!["pageNumber"] = 1;
        pages[0]!["spans"] = JsonSerializer.SerializeToNode(new[] { new { offset = 0, length = 40 } });
        second["pageNumber"] = 2;
        second["spans"] = JsonSerializer.SerializeToNode(new[] { new { offset = 40, length = 40 } });
        pages.Add(second);
        content["sections"] = new JsonArray(AzureWireFixture.Section("/paragraphs/0", "/paragraphs/1", "/paragraphs/4"));
        if (nested)
        {
            content["sections"]!.AsArray().Add(AzureWireFixture.Section("/sections/0"));
        }

        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        Assert.Equal("Title | Body one | Page 1 of 2 | Header two | Body two",
            string.Join(" | ", result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(Text)));
        Assert.Equal(2, result.Sections.Count);
        var first = Assert.IsType<ExtractionDocumentSection>(result.Sections[0].Elements[0]);
        var continuation = Assert.IsType<ExtractionDocumentSection>(result.Sections[1].Elements[1]);
        Assert.NotSame(first, continuation);
        Assert.Equal(2, first.Sources.Count);
        Assert.All(first.Sources, source => Assert.Contains(source, continuation.Sources));
        Assert.Same(first.Sources[0].GetRawRepresentation(), continuation.Sources[0].GetRawRepresentation());
        Assert.Equal("Page 1 of 2", Text(Assert.IsType<ExtractionDocumentTextElement>(result.Sections[0].Elements[1])));
        Assert.Equal("Header two", Text(Assert.IsType<ExtractionDocumentTextElement>(result.Sections[1].Elements[0])));
        if (nested)
        {
            first = Assert.IsType<ExtractionDocumentSection>(Assert.Single(first.Elements));
            continuation = Assert.IsType<ExtractionDocumentSection>(Assert.Single(continuation.Elements));
        }

        Assert.Equal(new[] { "Title", "Body one" }, first.Elements.Cast<ExtractionDocumentTextElement>().Select(Text));
        Assert.Equal("Body two", Text(Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(continuation.Elements))));
        Assert.Equal(new int?[] { 1, 1, 1, 2, 2 }, result.EnumerateContent()
            .Select(element => Assert.Single(element.Sources).Page.PageNumber));
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Theory]
    [InlineData("disjoint")]
    [InlineData("overlap")]
    [InlineData("unknown")]
    [InlineData("unknown-section")]
    [InlineData("interleaved")]
    public async Task ExtractAsync_NativeGroups_DoNotAdoptUnownedParagraphs(string scenario)
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["tables"] = new JsonArray();
        content["figures"] = new JsonArray();
        JsonNode template = content["paragraphs"]![0]!.DeepClone();
        var paragraphs = new JsonArray();
        for (int index = 0; index < 4; index++)
        {
            JsonNode paragraph = template.DeepClone();
            paragraph["content"] = ((char)('A' + index)).ToString();
            paragraph.AsObject().Remove("role");
            SetSpan(paragraph, index * 10, 1);
            if (scenario == "unknown" && index == 1)
            {
                paragraph.AsObject().Remove(AzureWireFixture.IsContentUnderstanding ? "span" : "spans");
            }

            paragraphs.Add(paragraph);
        }

        content["paragraphs"] = paragraphs;
        JsonObject native = AzureWireFixture.Section("/paragraphs/0", "/paragraphs/2", "/paragraphs/3");
        if (scenario == "disjoint" && !AzureWireFixture.IsContentUnderstanding)
        {
            native["spans"] = JsonSerializer.SerializeToNode(new[]
            {
                new { offset = 0, length = 1 }, new { offset = 20, length = 11 },
            });
        }
        else if (scenario == "unknown-section")
        {
            native.Remove(AzureWireFixture.IsContentUnderstanding ? "span" : "spans");
        }

        content["sections"] = scenario == "interleaved"
            ? new JsonArray(AzureWireFixture.Section("/paragraphs/0", "/paragraphs/2"),
                AzureWireFixture.Section("/paragraphs/1", "/paragraphs/3"))
            : new JsonArray(native);
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var groups = result.Sections[0].Elements.OfType<ExtractionDocumentSection>().ToArray();
        Assert.Equal(scenario == "interleaved" ? new[] { "A", "C", "B", "D" } : new[] { "A", "C", "D", "B" },
            result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(Text));
        Assert.Equal(scenario == "interleaved" ? 2 : 1, groups.Length);
        Assert.DoesNotContain(groups[0].Elements, element =>
            element is ExtractionDocumentTextElement paragraph && Text(paragraph) == "B");
        Assert.All(groups[0].Elements, element => Assert.NotEmpty(element.Sources));
        if (scenario == "unknown-section")
        {
            Assert.Empty(groups[0].Sources);
        }
        Assert.IsType<NativeResult>(result.RawRepresentation);
    }

    [Fact]
    public async Task ExtractAsync_FigureElements_BecomeImageText()
    {
        var root = FigureFixture();
        var content = AzureWireFixture.Content(root);
        JsonNode second = content["paragraphs"]![1]!.DeepClone();
        second["content"] = "Q2";
        SetSpan(second, 23, 2);
        content["paragraphs"]!.AsArray().Add(second);
        content["figures"]![0]!["elements"] = new JsonArray("/paragraphs/1", "/paragraphs/2");
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var image = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentImageElement>());
        Assert.Equal("Q1\nQ2", image.Text);
        Assert.Equal("Revenue chart", image.AlternativeText);
        Assert.Null(image.Content);
        Assert.Equal(new[] { "Invoice heading" }, result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(Text));
#if CONTENT_UNDERSTANDING
        var native = Assert.IsType<Azure.AI.ContentUnderstanding.DocumentContent>(
            Assert.Single(Assert.IsType<NativeResult>(result.RawRepresentation).Contents));
#else
        var native = Assert.IsType<NativeResult>(result.RawRepresentation);
#endif
        Assert.Same(native.Figures[0], image.Sources[0].GetRawRepresentation());
        Assert.Contains(image.Sources, source => ReferenceEquals(native.Paragraphs[1], source.GetRawRepresentation()));
        Assert.Contains(image.Sources, source => ReferenceEquals(native.Paragraphs[2], source.GetRawRepresentation()));
        Assert.Equal(AzureWireFixture.IsContentUnderstanding ? 7 : 3, image.Sources.Count);
        Assert.All(image.Sources, source => Assert.Same(result.SourcePages[0], source.Page));
        Assert.Equal(AzureWireFixture.Points, image.Sources[0].Polygon.Select(point => (point.X, point.Y)));
        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
        input.Position = 0;
        Assert.Equal(1, input.ReadByte());
    }

    [Theory]
    [InlineData("repeated")]
    [InlineData("section")]
    [InlineData("caption")]
    [InlineData("caption-span")]
    [InlineData("footnote")]
    [InlineData("footnote-span")]
    [InlineData("description")]
    [InlineData("two-figures")]
    [InlineData("cell")]
    [InlineData("cell-span")]
    [InlineData("missing-geometry")]
    [InlineData("outside")]
    [InlineData("concave")]
    [InlineData("role")]
    [InlineData("unresolved")]
    public async Task ExtractAsync_FigureOwnership_PreservesAmbiguousText(string scenario)
    {
        var root = FigureFixture();
        var content = AzureWireFixture.Content(root);
        JsonNode figure = content["figures"]![0]!;
        JsonNode paragraph = content["paragraphs"]![1]!;
        switch (scenario)
        {
            case "repeated":
                figure["elements"] = new JsonArray("/paragraphs/1", "/paragraphs/1");
                break;
            case "section":
                content["sections"] = new JsonArray(AzureWireFixture.Section("/paragraphs/0", "/paragraphs/1", "/figures/0"));
                break;
            case "caption":
                figure["caption"] = new JsonObject
                {
                    ["content"] = "Q1",
                    ["elements"] = new JsonArray("/paragraphs/1"),
                };
                break;
            case "caption-span":
                var caption = new JsonObject();
                SetSpan(caption, 20, 2);
                figure["caption"] = caption;
                break;
            case "footnote":
                figure["footnotes"] = new JsonArray(new JsonObject
                {
                    ["content"] = "Q1", ["elements"] = new JsonArray("/paragraphs/1"),
                });
                break;
            case "footnote-span":
                var footnote = new JsonObject();
                SetSpan(footnote, 20, 2);
                figure["footnotes"] = new JsonArray(footnote);
                break;
            case "description":
                if (AzureWireFixture.IsContentUnderstanding)
                {
                    figure["description"] = "A description about Q1.";
                }
                else
                {
                    figure["caption"] = new JsonObject { ["content"] = "A caption about Q1." };
                }
                break;
            case "two-figures":
                content["figures"]!.AsArray().Add(figure.DeepClone());
                break;
            case "cell":
                content["tables"] = AzureWireFixture.Content(AzureWireFixture.Create())["tables"]!.DeepClone();
                content["tables"]![0]!["cells"]![0]!["elements"] = new JsonArray("/paragraphs/1");
                break;
            case "cell-span":
                content["tables"] = AzureWireFixture.Content(AzureWireFixture.Create())["tables"]!.DeepClone();
                break;
            case "missing-geometry":
                SetSource(paragraph, 3, []);
                break;
            case "outside":
                SetSource(paragraph, 3, [20, 20, 30, 20, 30, 30, 20, 30]);
                break;
            case "concave":
                SetSource(figure, 3, [0, 0, 4, 0, 1, 1, 4, 4, 0, 4]);
                break;
            case "role":
                paragraph["role"] = "pageFooter";
                break;
            case "unresolved":
                figure["elements"] = new JsonArray("/paragraphs/999");
                break;
        }

        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var images = result.EnumerateContent().OfType<ExtractionDocumentImageElement>().ToArray();
        Assert.Equal(scenario == "two-figures" ? 2 : 1, images.Length);
        Assert.All(images, image => Assert.Equal(scenario == "repeated" ? "Q1" : null, image.Text));
        var text = result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(Text).ToArray();
        Assert.Equal(scenario is "repeated" or "cell" ? new[] { "Invoice heading" } : new[] { "Invoice heading", "Q1" }, text);
        if (scenario == "cell")
        {
            var table = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTableElement>());
            var cell = Assert.IsType<ExtractionDocumentSection>(table.Cells[0, 0]!.Content);
            Assert.Equal("Q1", Text(Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(cell.Elements))));
        }
        else if (scenario == "repeated")
        {
            Assert.Equal(AzureWireFixture.IsContentUnderstanding ? 4 : 2, images[0].Sources.Count);
        }

        Assert.Equal(2, transport.Delivered[^1].DisposeCount);
    }

    [Fact]
    public async Task ExtractAsync_FigureCaptionSpanOverlap_DoesNotClassifyAsOcr()
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["tables"] = new JsonArray();
        JsonNode paragraph = content["paragraphs"]![0]!.DeepClone();
        paragraph["content"] = "Q1";
        paragraph.AsObject().Remove("role");
        content["paragraphs"]!.AsArray().Add(paragraph);
        content["figures"]![0]!["elements"] = new JsonArray("/paragraphs/1");
        var caption = new JsonObject { ["content"] = "Revenue chart" };
        SetSpan(caption, 0, 80);
        content["figures"]![0]!["caption"] = caption;
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        Assert.Null(Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentImageElement>()).Text);
        Assert.Equal(new[] { "Invoice heading", "Q1" },
            result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(Text));
    }

    [Theory]
    [InlineData("clockwise", true)]
    [InlineData("no-bounds", false)]
    [InlineData("degenerate", false)]
    [InlineData("other-page", false)]
    public async Task ExtractAsync_FigureGeometry_RequiresReportedContainment(string scenario, bool recognized)
    {
        var root = FigureFixture();
        var content = AzureWireFixture.Content(root);
        JsonNode figure = content["figures"]![0]!;
        switch (scenario)
        {
            case "clockwise":
                SetSource(figure, 3, [0, 0, 0, 10, 10, 10, 10, 0]);
                break;
            case "no-bounds":
                SetSource(figure, 3, []);
                break;
            case "degenerate":
                SetSource(figure, 3, [0, 0, 1, 1, 2, 2]);
                break;
            case "other-page":
                JsonNode page = content["pages"]![0]!.DeepClone();
                page["pageNumber"] = 4;
                content["pages"]!.AsArray().Add(page);
                SetSource(figure, 4, AzureWireFixture.Coordinates);
                break;
        }

        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        var image = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentImageElement>());
        Assert.Equal(recognized ? "Q1" : null, image.Text);
        Assert.Equal(recognized ? new[] { "Invoice heading" } : new[] { "Invoice heading", "Q1" },
            result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(Text));
    }

#if CONTENT_UNDERSTANDING
    [Fact]
    public async Task ExtractAsync_MultipleContents_DoNotCompareIndependentOffsets()
    {
        var root = AzureWireFixture.Create();
        var first = AzureWireFixture.Content(root);
        first["tables"] = new JsonArray();
        first["figures"] = new JsonArray();
        first["paragraphs"]![0]!["content"] = "First";
        first["paragraphs"]![0]!.AsObject().Remove("source");
        SetSpan(first["paragraphs"]![0]!, 20, 5);
        var second = first.DeepClone();
        second["pages"]![0]!["pageNumber"] = 4;
        second["paragraphs"]![0]!["content"] = "Second";
        SetSpan(second["paragraphs"]![0]!, 0, 6);
        root["result"]!["contents"]!.AsArray().Add(second);
        var transport = Replay(AzureWireFixture.Bytes(root));
        using var client = Create(transport);
        using var input = new MemoryStream([1, 2, 3]);
        var result = await Extract(client, input);
        Assert.Equal(new[] { "First", "Second" }, result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(Text));
        Assert.Equal(2, result.SourcePages.Count);
        Assert.All(result.EnumerateContent(), element => Assert.Empty(element.Sources));
    }
#endif

    private static JsonObject FigureFixture()
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["tables"] = new JsonArray();
        JsonNode paragraph = content["paragraphs"]![0]!.DeepClone();
        SetSpan(content["paragraphs"]![0]!, 0, 15);
        paragraph["content"] = "Q1";
        paragraph.AsObject().Remove("role");
        SetSpan(paragraph, 20, 2);
        content["paragraphs"]!.AsArray().Add(paragraph);
        content["figures"]![0]!["elements"] = new JsonArray("/paragraphs/1");
        SetSpan(content["figures"]![0]!, 20, 5);
        if (!AzureWireFixture.IsContentUnderstanding)
        {
            SetSpan(content["figures"]![0]!["caption"]!, 60, 13);
        }
        return root;
    }

    private static string Text(ExtractionDocumentTextElement element) => Assert.IsType<TextContent>(element.Content).Text;

    private static void SetSpan(JsonNode element, int offset, int length)
    {
        var span = new JsonObject { ["offset"] = offset, ["length"] = length };
        element[AzureWireFixture.IsContentUnderstanding ? "span" : "spans"] =
            AzureWireFixture.IsContentUnderstanding ? span : new JsonArray(span);
    }

    private static void SetSource(JsonNode element, int page, double[] polygon)
    {
        if (AzureWireFixture.IsContentUnderstanding)
        {
            element["source"] = polygon.Length == 0 ? $"D({page})" :
                $"D({page},{string.Join(",", polygon.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)))})";
        }
        else
        {
            element["boundingRegions"] = JsonSerializer.SerializeToNode(new[] { new { pageNumber = page, polygon } });
        }
    }
}
