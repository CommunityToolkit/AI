// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.VisionLanguageModel;

/// <summary>Extracts documents with a vision language model through a borrowed <see cref="IChatClient"/> that supports the supplied input format.</summary>
/// <remarks>Structured mode fails explicitly when the model cannot satisfy its schema; it never falls back to free-form.</remarks>
public sealed class VisionLanguageModelDocumentExtractionClient : DocumentExtractionClient
{
    private const string ProviderName = "vision-llm";
    private const int MaximumTableGridPositions = 65_536;
    private const string DefaultPrompt =
        "Transcribe this document to GitHub-flavored Markdown. Preserve headings, lists, and tables. " +
        "Output only the Markdown, no commentary.";
    private const string StructuredPrompt =
        "Transcribe this document as a JSON object with a pages array. Do not infer page numbers. " +
        "Each page may contain markdown, tables with either cells or literal markdown, " +
        "figures with captions, language, and confidence. " +
        "An index is an explicitly known zero-based original page number, not an assembly index. " +
        "For cells, rowIndex and columnIndex are zero-based anchor positions including header rows and columns. " +
        "rowSpan and columnSpan are positive counts including the anchor, defaulting to one. " +
        "Emit each merged cell once at its top-left anchor; never emit covered positions as separate cells. " +
        "Include observed blank cells with null or empty text; do not invent missing cells or content. " +
        "Use role for observed header semantics, such as columnHeader or rowHeader. " +
        "Do not output rowCount or columnCount: grid dimensions are derived from anchors and spans. " +
        "Do not mix table markdown with cells or repeat table text in page markdown. " +
        "For a literal Markdown table, use cells:null; for addressed cells, use markdown:null. " +
        "The combined derived table grids must not exceed 65536 positions in this response.";
    private static readonly JsonElement s_schema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type":"object","required":["pages"],"additionalProperties":false,
          "properties":{"pages":{"type":"array","items":{
            "type":"object","additionalProperties":false,"properties":{
              "index":{"type":["integer","null"],"minimum":0},
              "markdown":{"type":["string","null"]},
              "tables":{"type":"array","items":{"type":"object","additionalProperties":false,
                "description":"Use cells or literal markdown, not both. Dimensions come only from cell anchors/spans.",
                "properties":{
                  "markdown":{"type":["string","null"]},"cells":{"type":["array","null"],"maxItems":65536,"items":{"type":"object",
                    "additionalProperties":false,
                    "required":["rowIndex","columnIndex"],
                    "properties":{"rowIndex":{"type":"integer","minimum":0,"description":"Zero-based anchor row, including headers."},
                      "columnIndex":{"type":"integer","minimum":0,"description":"Zero-based anchor column, including headers."},
                      "text":{"type":["string","null"]},"rowSpan":{"type":"integer","minimum":1},
                      "columnSpan":{"type":"integer","minimum":1},"role":{"type":["string","null"]}}}}}}},
              "figures":{"type":"array","items":{"type":"object","additionalProperties":false,"properties":{"caption":{"type":["string","null"]}}}},
              "language":{"type":["string","null"]},"confidence":{"type":["number","null"],"minimum":0,"maximum":1}
            }
          }}}
        }
        """);

    private readonly IChatClient _chatClient;
    private readonly string? _prompt;
    private readonly bool _useStructuredOutput;

    /// <summary>Creates an adapter without taking ownership of the chat client.</summary>
    public VisionLanguageModelDocumentExtractionClient(IChatClient chatClient, VisionLanguageModelDocumentExtractionClientOptions? options = null)
    {
        Throw.IfNull(chatClient);
        _chatClient = chatClient;
        _prompt = options?.Prompt;
        _useStructuredOutput = options?.UseStructuredOutput ?? false;
    }

    /// <inheritdoc/>
    public override async Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType,
        DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        DocumentExtractionOptions request = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(request, nameof(VisionLanguageModelDocumentExtractionClient));
        bool isPdf = string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Vision extraction supports image/* and application/pdf inputs.");
        }

        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        byte[] bytes = buffer.ToArray();
        var output = new ExtractionDocument(identifier);
        var chatOptions = new ChatOptions
        {
            ModelId = request.ExtractorName,
            ResponseFormat = _useStructuredOutput ? ChatResponseFormat.ForJsonSchema(s_schema) : null,
        };
        string instruction = _prompt ?? (_useStructuredOutput ? StructuredPrompt : DefaultPrompt);
        var message = new ChatMessage(ChatRole.User, [new TextContent(instruction), new DataContent(bytes, mediaType)]);
        ChatResponse response = await _chatClient.GetResponseAsync([message], chatOptions, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        output.RawRepresentation = response;
        string framePrefix = $"{output.Identifier}:{ProviderName}:response:0";
        if (!_useStructuredOutput)
        {
            var reference = CreateFrame(null, isPdf ? "document" : "image");
            var section = new ExtractionDocumentSection();
            section.Sources.Add(reference);
            output.Sections.Add(section);
            AddText(section, response.Text, reference);
            cancellationToken.ThrowIfCancellationRequested();
            return output;
        }

        using JsonDocument json = ParseResponse(response.Text);
        ValidateObject(json.RootElement, "response", "pages");
        if (!json.RootElement.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Vision structured output requires a pages array.");
        }

        var seen = new HashSet<int>();
        int frameIndex = 0;
        int remainingGridPositions = MaximumTableGridPositions;
        foreach (JsonElement nativePage in pages.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string pageContext = $"pages[{frameIndex}]";
            ValidateObject(nativePage, pageContext, "index", "markdown", "tables", "figures", "language", "confidence");
            int? reportedNumber = null;
            if (nativePage.TryGetProperty("index", out JsonElement index) && index.ValueKind != JsonValueKind.Null)
            {
                int value = ReadInteger(index, $"{pageContext}.index", 0);
                if (value == int.MaxValue)
                {
                    throw Invalid($"{pageContext}.index", "cannot convert to a one-based page number.");
                }

                reportedNumber = value + 1;
            }

            int? number = reportedNumber;
            if (number is { } numbered && !seen.Add(numbered))
            {
                throw Invalid(pageContext, $"duplicate page number {numbered}.");
            }

            if (nativePage.TryGetProperty("confidence", out JsonElement confidence) && confidence.ValueKind != JsonValueKind.Null)
            {
                if (confidence.ValueKind != JsonValueKind.Number || !confidence.TryGetDouble(out double value) ||
                    double.IsNaN(value) || double.IsInfinity(value) || value is < 0 or > 1)
                {
                    throw Invalid($"{pageContext}.confidence", "must be a finite number in [0,1].");
                }
            }

            if (nativePage.TryGetProperty("language", out JsonElement language) &&
                language.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                throw Invalid($"{pageContext}.language", "must be a string or null.");
            }

            var reference = CreateFrame(number, $"frame:{frameIndex++}");
            var section = new ExtractionDocumentSection();
            section.Sources.Add(reference);
            output.Sections.Add(section);
            AddText(section, GetString(nativePage, "markdown", pageContext), reference);
            int tableIndex = 0;
            foreach (JsonElement table in GetArray(nativePage, "tables", pageContext))
            {
                var mapped = MapTable(table, $"{pageContext}.tables[{tableIndex++}]", reference,
                    ref remainingGridPositions, cancellationToken);
                ProviderAdapterHelpers.AddElement(section, mapped, [reference]);
            }

            int figureIndex = 0;
            foreach (JsonElement figure in GetArray(nativePage, "figures", pageContext))
            {
                string figureContext = $"{pageContext}.figures[{figureIndex++}]";
                ValidateObject(figure, figureContext, "caption");
                string? caption = GetString(figure, "caption", figureContext);
                ProviderAdapterHelpers.AddElement(section,
                    new ExtractionDocumentImageElement() { AlternativeText = caption }, [reference]);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return output;

        ExtractionDocumentSourceReference CreateFrame(int? number, string name)
        {
            var page = new ExtractionDocumentSourcePage($"{framePrefix}:{name}", number);
            output.SourcePages.Add(page);
            return new ExtractionDocumentSourceReference(page, rawRepresentation: response.RawRepresentation ?? response);
        }
    }

    private static void AddText(ExtractionDocumentSection section, string? text, ExtractionDocumentSourceReference reference)
    {
        if (!string.IsNullOrEmpty(text))
        {
            ProviderAdapterHelpers.AddElement(section, new ExtractionDocumentTextElement(new TextContent(text)), [reference]);
        }
    }

    private static ExtractionDocumentTableElement MapTable(
        JsonElement table, string context, ExtractionDocumentSourceReference reference,
        ref int remainingGridPositions, CancellationToken cancellationToken)
    {
        ValidateObject(table, context, "cells", "markdown");
        string? markdown = GetString(table, "markdown", context);
        bool hasCells = table.TryGetProperty("cells", out JsonElement cells) && cells.ValueKind != JsonValueKind.Null;
        if (!hasCells)
        {
            if (markdown is null)
            {
                throw Invalid(context, "requires cells or literal markdown.");
            }

            return new ExtractionDocumentTableElement(new TextContent(markdown), 0, 0);
        }

        if (markdown is not null)
        {
            throw Invalid(context, "cannot combine cells and markdown.");
        }

        if (cells.ValueKind != JsonValueKind.Array || cells.GetArrayLength() > MaximumTableGridPositions)
        {
            throw Invalid($"{context}.cells", $"must be an array with at most {MaximumTableGridPositions} anchors.");
        }

        var parsed = new List<(int Row, int Column, ExtractionDocumentTableCell Cell)>();
        var anchors = new HashSet<(int, int)>();
        int rows = 0;
        int columns = 0;
        int cellIndex = 0;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string cellContext = $"{context}.cells[{cellIndex++}]";
            ValidateObject(cell, cellContext, "rowIndex", "columnIndex", "rowSpan", "columnSpan", "text", "role");
            int row = GetInteger(cell, "rowIndex", cellContext, 0);
            int column = GetInteger(cell, "columnIndex", cellContext, 0);
            int rowSpan = GetInteger(cell, "rowSpan", cellContext, 1, 1);
            int columnSpan = GetInteger(cell, "columnSpan", cellContext, 1, 1);
            if (!anchors.Add((row, column)))
            {
                throw Invalid(cellContext, $"duplicate anchor [{row},{column}].");
            }

            long endRow = (long)row + rowSpan;
            long endColumn = (long)column + columnSpan;
            if (endRow > int.MaxValue || endColumn > int.MaxValue)
            {
                throw Invalid(cellContext, "anchor plus span overflows the supported grid extent.");
            }

            rows = Math.Max(rows, (int)endRow);
            columns = Math.Max(columns, (int)endColumn);
            if ((long)rows * columns > remainingGridPositions)
            {
                throw Invalid(cellContext, $"derived grids exceed the response limit of {MaximumTableGridPositions} positions.");
            }

            string? text = GetString(cell, "text", cellContext);
            string? role = GetString(cell, "role", cellContext);
            var mappedCell = text is null
                ? new ExtractionDocumentTableCell(rowSpan, columnSpan, role)
                : new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent(text)), rowSpan, columnSpan, role);
            mappedCell.Sources.Add(reference);
            mappedCell.Content?.Sources.Add(reference);
            parsed.Add((row, column, mappedCell));
        }

        // Validate occupied positions before allocating the shared rectangular grid.
        var occupied = new HashSet<(int, int)>();
        for (int i = 0; i < parsed.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (row, column, cell) = parsed[i];
            for (int r = row; r < row + cell.RowSpan; r++)
            {
                for (int c = column; c < column + cell.ColumnSpan; c++)
                {
                    if (!occupied.Add((r, c)))
                    {
                        throw Invalid($"{context}.cells[{i}]", $"overlaps another cell at [{r},{c}].");
                    }
                }
            }
        }

        remainingGridPositions -= rows * columns;
        var mapped = new ExtractionDocumentTableElement(null, rows, columns);
        foreach (var (row, column, cell) in parsed)
        {
            mapped.Cells[row, column] = cell;
        }

        return mapped;
    }

    private static JsonDocument ParseResponse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Vision response is invalid JSON at {error.Path ?? "$"}.", error);
        }
    }

    private static void ValidateObject(JsonElement element, string context, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(context, "must be an object.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Invalid(context, $"duplicate property '{property.Name}'.");
            }

            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw Invalid(context, $"unsupported property '{property.Name}'.");
            }
        }
    }

    private static InvalidDataException Invalid(string context, string message)
        => new($"Vision {context}: {message}");

    private static string? GetString(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw Invalid($"{context}.{name}", "must be a string or null.");
    }

    private static int GetInteger(JsonElement element, string name, string context, int minimum, int? defaultValue = null)
        => element.TryGetProperty(name, out JsonElement value)
            ? ReadInteger(value, $"{context}.{name}", minimum)
            : defaultValue ?? throw Invalid($"{context}.{name}", "is required.");

    private static int ReadInteger(JsonElement value, string context, int minimum)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= minimum
            ? number : throw Invalid(context, $"must be an integer in [{minimum},{int.MaxValue}].");

    private static IEnumerable<JsonElement> GetArray(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{context}.{name}", "must be an array.");
        }

        return value.EnumerateArray();
    }

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);
        return base.GetService(serviceType, serviceKey) ?? _chatClient.GetService(serviceType, serviceKey);
    }
}
