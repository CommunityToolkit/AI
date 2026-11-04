// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig;

namespace CommunityToolkit.DocumentExtraction.Vision;

/// <summary>Extracts documents with a borrowed vision-capable <see cref="IChatClient"/>.</summary>
/// <remarks>Structured mode fails explicitly when the model cannot satisfy its schema; it never falls back to free-form.</remarks>
public sealed class VisionLlmOcrClient : DocumentExtractionClient
{
    private const string ProviderName = "vision-llm";
    private const string DefaultPrompt =
        "Transcribe this image to GitHub-flavored Markdown. Preserve headings, lists, and tables. " +
        "Output only the Markdown, no commentary.";
    private const string StructuredPrompt =
        "Transcribe this image as a JSON object with a pages array. Do not infer page numbers. " +
        "Each page may contain markdown, tables with rowCount/columnCount and either markdown or cells " +
        "(rowIndex, columnIndex, text, rowSpan, columnSpan, role), figures with captions, language, and confidence. " +
        "An index is an explicitly known zero-based original page number, not an assembly index. " +
        "Do not include a table's text again in page markdown when cells are supplied.";
    private static readonly JsonElement s_schema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type":"object","required":["pages"],"additionalProperties":false,
          "properties":{"pages":{"type":"array","items":{
            "type":"object","properties":{
              "index":{"type":["integer","null"],"minimum":0},
              "markdown":{"type":["string","null"]},
              "tables":{"type":"array","items":{"type":"object","required":["rowCount","columnCount"],
                "properties":{"rowCount":{"type":"integer","minimum":0},"columnCount":{"type":"integer","minimum":0},
                  "markdown":{"type":["string","null"]},"cells":{"type":"array","items":{"type":"object",
                    "required":["rowIndex","columnIndex"],
                    "properties":{"rowIndex":{"type":"integer","minimum":0},"columnIndex":{"type":"integer","minimum":0},
                      "text":{"type":["string","null"]},"rowSpan":{"type":"integer","minimum":1},
                      "columnSpan":{"type":"integer","minimum":1},"role":{"type":["string","null"]}}}}}}},
              "figures":{"type":"array","items":{"type":"object","properties":{"caption":{"type":["string","null"]}}}},
              "language":{"type":["string","null"]},"confidence":{"type":["number","null"],"minimum":0,"maximum":1}
            }
          }}}
        }
        """);

    private readonly IChatClient _chatClient;
    private readonly string? _prompt;
    private readonly bool _useStructuredOutput;

    /// <summary>Creates an adapter without taking ownership of the chat client.</summary>
    public VisionLlmOcrClient(IChatClient chatClient, string? prompt = null, bool useStructuredOutput = false)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _prompt = prompt;
        _useStructuredOutput = useStructuredOutput;
    }

    /// <inheritdoc/>
    public override async Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType,
        DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        DocumentExtractionOptions request = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(request, nameof(VisionLlmOcrClient));
        bool isPdf = string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Vision extraction supports image/* and application/pdf inputs.");
        }

        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        byte[] bytes = buffer.ToArray();
        var output = new ExtractionDocument(identifier);
        var responses = new List<ChatResponse>();
        if (isPdf)
        {
            using var pdfInput = new MemoryStream(bytes, writable: false);
            using PdfDocument pdf = PdfDocument.Open(pdfInput);
            for (int number = 1; number <= pdf.NumberOfPages; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pdf.GetPage(number);
                await ExtractPageAsync(output, PdfPageRasterizer.RenderPage(bytes, number),
                    "image/png", request, responses, number, page.Width, page.Height, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await ExtractPageAsync(output, bytes, mediaType, request, responses, null, null, null, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        output.RawRepresentation = responses.Count == 1 ? responses[0] : responses.ToArray();
        return output;
    }

    private async Task ExtractPageAsync(
        ExtractionDocument output, byte[] bytes, string mediaType, DocumentExtractionOptions request,
        List<ChatResponse> responses,
        int? originalPageNumber, double? width, double? height, CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            ModelId = request.ExtractorName,
            ResponseFormat = _useStructuredOutput ? ChatResponseFormat.ForJsonSchema(s_schema) : null,
        };
        string instruction = _prompt ?? (_useStructuredOutput ? StructuredPrompt : DefaultPrompt);
        if (_useStructuredOutput && originalPageNumber is { } known)
        {
            instruction += $" This image is original page index {known - 1}. Return exactly one page.";
        }

        var message = new ChatMessage(ChatRole.User, [new TextContent(instruction), new DataContent(bytes, mediaType)]);
        ChatResponse response = await _chatClient.GetResponseAsync([message], options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        string framePrefix = $"{output.Identifier}:{ProviderName}:response:{responses.Count}";
        responses.Add(response);
        if (!_useStructuredOutput)
        {
            var (page, reference) = CreateFrame(originalPageNumber, "image");
            var section = new ExtractionDocumentSection();
            section.Sources.Add(reference);
            output.Sections.Add(section);
            AddText(section, response.Text, reference);
            return;
        }

        using JsonDocument json = JsonDocument.Parse(response.Text);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Vision structured output requires a pages array.");
        }

        foreach (JsonProperty property in json.RootElement.EnumerateObject())
        {
            if (property.Name != "pages")
            {
                throw new InvalidDataException($"Vision structured output contains unsupported root property '{property.Name}'.");
            }
        }

        if (originalPageNumber is not null && pages.GetArrayLength() != 1)
        {
            throw new InvalidDataException("Per-page PDF vision extraction must return exactly one page.");
        }

        var seen = new HashSet<int>();
        int frameIndex = 0;
        foreach (JsonElement nativePage in pages.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? reportedNumber = null;
            if (nativePage.TryGetProperty("index", out JsonElement index) && index.ValueKind != JsonValueKind.Null)
            {
                int value = index.GetInt32();
                if (value < 0)
                {
                    throw new InvalidDataException("Vision page indices must be non-negative.");
                }

                reportedNumber = checked(value + 1);
            }

            if (originalPageNumber is not null && reportedNumber is not null && originalPageNumber != reportedNumber)
            {
                throw new InvalidDataException("Vision returned a page index inconsistent with the source PDF.");
            }

            int? number = originalPageNumber ?? reportedNumber;
            if (number is { } numbered && !seen.Add(numbered))
            {
                throw new InvalidDataException($"Vision returned duplicate page number {numbered}.");
            }

            if (nativePage.TryGetProperty("confidence", out JsonElement confidence) && confidence.ValueKind != JsonValueKind.Null)
            {
                double value = confidence.GetDouble();
                if (!double.IsFinite(value) || value is < 0 or > 1)
                {
                    throw new InvalidDataException("Vision confidence must be in [0,1].");
                }
            }

            if (nativePage.TryGetProperty("language", out JsonElement language) &&
                language.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                throw new InvalidDataException("Vision language must be a string or null.");
            }

            var (_, reference) = CreateFrame(number, $"frame:{frameIndex++}");
            var section = new ExtractionDocumentSection();
            section.Sources.Add(reference);
            output.Sections.Add(section);
            AddText(section, GetString(nativePage, "markdown"), reference);
            foreach (JsonElement table in GetArray(nativePage, "tables"))
            {
                int rows = table.GetProperty("rowCount").GetInt32();
                int columns = table.GetProperty("columnCount").GetInt32();
                bool hasCells = table.TryGetProperty("cells", out JsonElement cells) && cells.ValueKind == JsonValueKind.Array;
                string? markdown = GetString(table, "markdown");
                var mapped = new ExtractionDocumentTableElement(
                    hasCells || markdown is null ? null : new TextContent(markdown), rows, columns);
                var seenCells = new HashSet<(int Row, int Column)>();
                foreach (JsonElement cell in GetArray(table, "cells"))
                {
                    int row = cell.GetProperty("rowIndex").GetInt32();
                    int column = cell.GetProperty("columnIndex").GetInt32();
                    if (!seenCells.Add((row, column)))
                    {
                        throw new InvalidDataException($"Vision returned duplicate table cell anchor [{row},{column}].");
                    }

                    string? text = GetString(cell, "text");
                    var mappedCell = text is null
                        ? new ExtractionDocumentTableCell(GetInteger(cell, "rowSpan"), GetInteger(cell, "columnSpan"), GetString(cell, "role"))
                        : new ExtractionDocumentTableCell(
                            new ExtractionDocumentTextElement(new TextContent(text)),
                            GetInteger(cell, "rowSpan"), GetInteger(cell, "columnSpan"), GetString(cell, "role"));
                    mappedCell.Sources.Add(reference);
                    mappedCell.Content?.Sources.Add(reference);
                    mapped.Cells[row, column] = mappedCell;
                }

                ProviderAdapterHelpers.AddElement(section, mapped, [reference]);
            }

            foreach (JsonElement figure in GetArray(nativePage, "figures"))
            {
                string? caption = GetString(figure, "caption");
                ProviderAdapterHelpers.AddElement(section,
                    new ExtractionDocumentImageElement() { AlternativeText = caption }, [reference]);
            }
        }

        (ExtractionDocumentSourcePage, ExtractionDocumentSourceReference) CreateFrame(int? number, string name)
        {
            var page = new ExtractionDocumentSourcePage(
                $"{framePrefix}:{name}", number,
                width is { } w && height is { } h ? new ExtractionDocumentPageDimensions(w, h) : null,
                width is not null ? ExtractionDocumentCoordinateUnit.Point : null,
                width is not null ? ExtractionDocumentCoordinateOrigin.BottomLeft : null);
            output.SourcePages.Add(page);
            return (page, new ExtractionDocumentSourceReference(page, rawRepresentation: response.RawRepresentation ?? response));
        }
    }

    private static void AddText(ExtractionDocumentSection section, string? text, ExtractionDocumentSourceReference reference)
    {
        if (!string.IsNullOrEmpty(text))
        {
            ProviderAdapterHelpers.AddElement(section, new ExtractionDocumentTextElement(new TextContent(text)), [reference]);
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static int GetInteger(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : 1;

    private static IEnumerable<JsonElement> GetArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Vision '{name}' must be an array.");
        }

        return value.EnumerateArray();
    }

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return base.GetService(serviceType, serviceKey) ?? _chatClient.GetService(serviceType, serviceKey);
    }
}
