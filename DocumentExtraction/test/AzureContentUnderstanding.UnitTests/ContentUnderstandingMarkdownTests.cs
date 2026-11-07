using System.Text.Json.Nodes;
using Azure.AI.ContentUnderstanding;
using CommunityToolkit.DocumentExtraction.Tests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.AzureContentUnderstanding.UnitTests;

public sealed class ContentUnderstandingMarkdownTests
{
    [Fact]
    public async Task ExtractAsync_ActualMarkdownOnlyResponse_PreservesWholeDocumentTextAndNativePageMetadata()
    {
        using var fixture = typeof(ContentUnderstandingMarkdownTests).Assembly.GetManifestResourceStream(
            "MarkdownOnlyDocument.json")!;
        using var bytes = new MemoryStream();
        await fixture.CopyToAsync(bytes);
        var terminal = new TrackedResponse(bytes.ToArray());
        var transport = new OfflineTransport(TrackedResponse.Accepted(), terminal);
        var sdk = CreateSdk(transport);
        using var client = new ContentUnderstandingExtractionClient(sdk);
        using var input = new MemoryStream([0, 1, 2, 3]);
        input.Position = 1;
        var result = await client.ExtractAsync(input, "usgs.pdf", "application/pdf");
        var native = Assert.IsType<AnalysisResult>(result.RawRepresentation);
        var content = Assert.IsType<DocumentContent>(Assert.Single(native.Contents));
        Assert.Equal(14779, content.Markdown.Length);
        Assert.Empty(content.Paragraphs);
        Assert.Empty(content.Tables);
        Assert.All(content.Pages, page => Assert.Empty(page.Lines));
        var element = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(result.EnumerateContent()));
        Assert.Equal(content.Markdown, Assert.IsType<TextContent>(element.Content).Text);
        Assert.Contains("Timan-Pechora", content.Markdown);
        Assert.Null(element.Role);
        Assert.Null(result.Content);
        Assert.Equal(new int?[] { 1, 2 }, result.SourcePages.Select(page => page.PageNumber));
        Assert.All(result.SourcePages, page =>
        {
            Assert.Equal(new ExtractionDocumentPageDimensions(8.5, 11), page.Dimensions);
            Assert.Equal(ExtractionDocumentCoordinateUnit.Inch, page.CoordinateUnit);
            Assert.Equal(ExtractionDocumentCoordinateOrigin.TopLeft, page.CoordinateOrigin);
        });
        Assert.Equal(2, element.Sources.Count);
        var wholeDocument = Assert.Single(result.Sections, section => section.Elements.Count > 0);
        Assert.Equal(element.Sources, wholeDocument.Sources);
        for (int i = 0; i < 2; i++)
        {
            Assert.Same(result.SourcePages[i], element.Sources[i].Page);
            Assert.Empty(element.Sources[i].Polygon);
            Assert.Same(content, element.Sources[i].GetRawRepresentation());
            var pageSection = Assert.Single(result.Sections, section =>
                section.Sources.Count == 1 && ReferenceEquals(section.Sources[0].Page, result.SourcePages[i]));
            Assert.Empty(pageSection.Elements);
            Assert.Same(content.Pages[i], pageSection.Sources[0].GetRawRepresentation());
        }

        Assert.Equal("prebuilt-document", native.AnalyzerId);
        Assert.Contains("prebuilt-document", transport.Requests[0].Uri.AbsolutePath);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(2, terminal.DisposeCount);
        client.Dispose();
        Assert.Same(sdk, client.GetService(typeof(ContentUnderstandingClient)));
        Assert.True(input.CanRead);
        Assert.Equal(input.Length, input.Position);
        Assert.Equal(content.Markdown, Assert.IsType<TextContent>(element.Content).Text);
    }

    [Theory]
    [InlineData("paragraphs")]
    [InlineData("tables")]
    [InlineData("figures")]
    [InlineData("lines")]
    public async Task ExtractAsync_AuthoritativeStructuredChildren_DoesNotAppendAggregateMarkdown(string retained)
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["markdown"] = "MUST NOT DUPLICATE AGGREGATE MARKDOWN";
        foreach (string name in new[] { "paragraphs", "tables", "figures" })
        {
            if (name != retained)
            {
                content[name] = new JsonArray();
            }
        }

        if (retained == "lines")
        {
            content["pages"]![0]!["lines"] = new JsonArray(new JsonObject
            {
                ["content"] = "Native line",
                ["source"] = "D(3)",
            });
        }

        var transport = new OfflineTransport(TrackedResponse.Accepted(), new TrackedResponse(AzureWireFixture.Bytes(root)));
        using var client = new ContentUnderstandingExtractionClient(CreateSdk(transport));
        using var input = new MemoryStream([1]);
        var result = await client.ExtractAsync(input, "structured.pdf", "application/pdf");
        var element = Assert.Single(result.EnumerateContent());
        Assert.Equal(retained switch
        {
            "tables" => typeof(ExtractionDocumentTableElement),
            "figures" => typeof(ExtractionDocumentImageElement),
            _ => typeof(ExtractionDocumentTextElement),
        }, element.GetType());
        Assert.DoesNotContain(result.EnumerateContent(), value => value.Content is TextContent
        {
            Text: "MUST NOT DUPLICATE AGGREGATE MARKDOWN",
        });
        Assert.Single(result.Sections);
        if (retained is "paragraphs" or "lines")
        {
            Assert.Equal(retained == "paragraphs" ? "Invoice heading" : "Native line",
                Assert.IsType<TextContent>(element.Content).Text);
        }
        else if (retained == "tables")
        {
            var table = Assert.IsType<ExtractionDocumentTableElement>(element);
            Assert.Equal("Total amount", Assert.IsType<TextContent>(table.Cells[0, 0]!.Content!.Content).Text);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExtractAsync_NoDetailedContentOrMarkdown_DoesNotInventText(string? markdown)
    {
        var root = MarkdownOnly(markdown);
        var transport = new OfflineTransport(TrackedResponse.Accepted(), new TrackedResponse(AzureWireFixture.Bytes(root)));
        using var client = new ContentUnderstandingExtractionClient(CreateSdk(transport));
        using var input = new MemoryStream([1]);
        var result = await client.ExtractAsync(input, "empty.pdf", "application/pdf");
        Assert.Empty(result.EnumerateContent());
        Assert.Single(result.SourcePages);
        Assert.Single(result.Sections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_MultipleNativeContents_OnlyFallbackOwnMarkdownAndReportedSources(bool noPages)
    {
        var root = AzureWireFixture.Create();
        var fallback = AzureWireFixture.Content(MarkdownOnly("  literal Markdown\n| raw | table |\n")).DeepClone();
        fallback["pages"]![0]!["pageNumber"] = 9;
        if (noPages)
        {
            fallback["pages"] = new JsonArray();
        }

        root["result"]!["contents"]!.AsArray().Add(fallback);
        var transport = new OfflineTransport(TrackedResponse.Accepted(), new TrackedResponse(AzureWireFixture.Bytes(root)));
        using var client = new ContentUnderstandingExtractionClient(CreateSdk(transport));
        using var input = new MemoryStream([1]);
        var result = await client.ExtractAsync(input, "multiple.pdf", "application/pdf");
        var native = Assert.IsType<AnalysisResult>(result.RawRepresentation);
        var fallbackNative = Assert.IsType<DocumentContent>(native.Contents[1]);
        var text = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTextElement>(),
            element => element.Content is TextContent { Text: "  literal Markdown\n| raw | table |\n" });
        Assert.Equal(noPages ? new int?[] { 3 } : [3, 9], result.SourcePages.Select(page => page.PageNumber));
        Assert.Equal(noPages ? 0 : 1, text.Sources.Count);
        Assert.All(text.Sources, source =>
        {
            Assert.Equal(9, source.Page.PageNumber);
            Assert.Empty(source.Polygon);
            Assert.Same(fallbackNative, source.GetRawRepresentation());
        });
        Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTableElement>());
        Assert.Equal(2, result.EnumerateContent().OfType<ExtractionDocumentTextElement>().Count());
        Assert.Null(result.Content);
    }

    private static ContentUnderstandingClient CreateSdk(OfflineTransport transport)
    {
        var options = new ContentUnderstandingClientOptions { Transport = transport };
        options.Retry.MaxRetries = 0;
        return new ContentUnderstandingClient(new Uri("https://offline.test"), new FixedCredential(), options);
    }

    private static JsonObject MarkdownOnly(string? markdown)
    {
        var root = AzureWireFixture.Create();
        var content = AzureWireFixture.Content(root);
        content["paragraphs"] = new JsonArray();
        content["tables"] = new JsonArray();
        content["figures"] = new JsonArray();
        content["markdown"] = markdown;
        return root;
    }
}
