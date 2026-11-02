// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.Mistral;

/// <summary>
/// An <see cref="IDocumentExtractionClient"/> backed by Mistral OCR on Azure AI Foundry.
/// </summary>
public sealed class FoundryMistralOcrClient : IDocumentExtractionClient
{
    private const string ProviderName = "foundry-mistral-ocr";

    private static readonly TokenRequestContext s_scope =
        new(["https://cognitiveservices.azure.com/.default"]);

    private readonly Uri _endpoint;
    private readonly TokenCredential _credential;
    private readonly string _defaultModel;
    private readonly bool _includeImages;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    /// <summary>Creates an adapter using the supplied endpoint, credential, and optional borrowed HTTP client.</summary>
    public FoundryMistralOcrClient(
        Uri endpoint,
        TokenCredential credential,
        string defaultModel = "mistral-ocr-4-0",
        bool includeImages = false,
        HttpClient? httpClient = null)
        : this(
            endpoint,
            credential,
            defaultModel,
            includeImages,
            httpClient,
            Task.Delay)
    {
    }

    internal FoundryMistralOcrClient(
        Uri endpoint,
        TokenCredential credential,
        string defaultModel,
        bool includeImages,
        HttpClient? httpClient,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(delayAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModel);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The endpoint must be an absolute HTTP or HTTPS URI.", nameof(endpoint));
        }
        _endpoint = endpoint;
        _credential = credential;
        _defaultModel = defaultModel;
        _includeImages = includeImages;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _ownsHttpClient = httpClient is null;
        _delayAsync = delayAsync;
    }

    /// <inheritdoc/>
    public async Task<ExtractionDocument> ExtractAsync(
        Stream document,
        string identifier,
        string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        DocumentExtractionOptions normalizedOptions = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(
            normalizedOptions,
            nameof(FoundryMistralOcrClient),
            "includeImages",
            "imageLimit",
            "imageMinSize");
        string model = ProviderAdapterHelpers.ResolveExtractor(
            normalizedOptions,
            _defaultModel,
            nameof(FoundryMistralOcrClient));
        bool includeImages = ProviderAdapterHelpers.GetProperty<bool?>(
            normalizedOptions,
            "includeImages") ?? _includeImages;

        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        string dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(buffer.ToArray())}";

        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["document"] = new JsonObject
            {
                ["type"] = "document_url",
                ["document_url"] = dataUrl,
            },
            ["include_image_base64"] = includeImages,
        };
        if (ProviderAdapterHelpers.GetProperty<int?>(normalizedOptions, "imageLimit") is int imageLimit)
        {
            if (imageLimit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "imageLimit must be non-negative.");
            }

            requestBody["image_limit"] = imageLimit;
        }

        if (ProviderAdapterHelpers.GetProperty<int?>(normalizedOptions, "imageMinSize") is int imageMinSize)
        {
            if (imageMinSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "imageMinSize must be non-negative.");
            }

            requestBody["image_min_size"] = imageMinSize;
        }

        byte[] responseBytes = await SendAsync(requestBody, cancellationToken).ConfigureAwait(false);
        using JsonDocument payload = JsonDocument.Parse(responseBytes);
        JsonElement detachedPayload = payload.RootElement.Clone();
        var output = new ExtractionDocument(identifier)
        {
            RawRepresentation = detachedPayload,
        };

            if (!payload.RootElement.TryGetProperty("pages", out JsonElement responsePages)
                || responsePages.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Mistral OCR response did not contain a pages array.");
            }

            var seenPages = new HashSet<int>();
            foreach (JsonElement page in responsePages.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                int index = page.GetProperty("index").GetInt32();
                int pageNumber = checked(index + 1);
                if (!seenPages.Add(pageNumber))
                {
                    throw new InvalidDataException($"Mistral OCR returned duplicate page number {pageNumber}.");
                }
                string markdown = page.TryGetProperty("markdown", out JsonElement markdownElement)
                    && markdownElement.ValueKind == JsonValueKind.String
                        ? markdownElement.GetString() ?? string.Empty
                        : string.Empty;

                (double? width, double? height) = GetDimensions(page);
                (ExtractionDocumentSourcePage sourcePage, _) =
                    ProviderAdapterHelpers.CreatePageFrame(
                        identifier,
                        ProviderName,
                        pageNumber,
                        width,
                        height,
                        width.HasValue ? ExtractionDocumentCoordinateUnit.Pixel : null,
                        width.HasValue ? ExtractionDocumentCoordinateOrigin.TopLeft : null);
                var pageReference = new ExtractionDocumentSourceReference(sourcePage, rawRepresentation: page.Clone());
                var section = new ExtractionDocumentSection();
                section.Sources.Add(pageReference);
                output.SourcePages.Add(sourcePage);
                output.Sections.Add(section);
                var replacements = new List<(int Start, int Length, ExtractionDocumentTableElement Table)>();
                var unplacedTables = new List<ExtractionDocumentTableElement>();

                if (page.TryGetProperty("tables", out JsonElement tables)
                    && tables.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement table in tables.EnumerateArray())
                    {
                        string? tableMarkdown = GetString(table, "content") ?? GetString(table, "markdown");
                        string? tableId = GetString(table, "id");
                        (int Start, int Length)? match = string.IsNullOrEmpty(tableId)
                            ? null
                            : FindUnique(markdown, $"[{tableId}]({tableId})");
                        if (match is null && !string.IsNullOrEmpty(tableMarkdown))
                        {
                            match = FindUnique(markdown, tableMarkdown);
                        }

                        if (match is { } candidate && replacements.Any(replacement =>
                            replacement.Start < candidate.Start + candidate.Length
                            && candidate.Start < replacement.Start + replacement.Length))
                        {
                            match = null;
                        }

                        var extractionTable = new ExtractionDocumentTableElement(tableMarkdown is null ? null : new TextContent(tableMarkdown), 0, 0);
                        extractionTable.Sources.Add(new ExtractionDocumentSourceReference(sourcePage, rawRepresentation: table.Clone()));
                        if (match is { } position)
                        {
                            replacements.Add((position.Start, position.Length, extractionTable));
                        }
                        else
                        {
                            unplacedTables.Add(extractionTable);
                        }

                    }
                }

                int cursor = 0;
                foreach ((int start, int length, ExtractionDocumentTableElement table) in
                    replacements.OrderBy(item => item.Start))
                {
                    AddText(markdown[cursor..start]);
                    AddTable(table);
                    cursor = start + length;
                }

                AddText(markdown[cursor..]);
                foreach (ExtractionDocumentTableElement table in unplacedTables)
                {
                    AddTable(table);
                }

                if (page.TryGetProperty("images", out JsonElement images)
                    && images.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement image in images.EnumerateArray())
                    {
                        ReadOnlyMemory<byte>? content = DecodeImage(image);
                        ExtractionDocumentSourceReference source = CreateImageSource(sourcePage, image);
                        ProviderAdapterHelpers.AddElement(section, new ExtractionDocumentImageElement()
                        {
                            Content = content is null ? null : new DataContent(content.Value, "image/png"),
                        }, [source]);
                    }
                }

                void AddTable(ExtractionDocumentTableElement table)
                {
                    ExtractionDocumentSourceReference[] sources = table.Sources.ToArray();
                    table.Sources.Clear();
                    ProviderAdapterHelpers.AddElement(section, table, sources);
                }

                void AddText(string value)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        ProviderAdapterHelpers.AddElement(section, new ExtractionDocumentTextElement(new TextContent(value)), [pageReference]);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return output;
    }

    private async Task<byte[]> SendAsync(JsonObject body, CancellationToken cancellationToken)
    {
        AccessToken token = await _credential
            .GetTokenAsync(s_scope, cancellationToken)
            .ConfigureAwait(false);
        string url = $"{_endpoint.ToString().TrimEnd('/')}/providers/mistral/azure/ocr";
        string json = body.ToJsonString();

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            byte[] responseBytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5)
            {
                await _delayAsync(TimeSpan.FromSeconds(attempt * 3), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Mistral OCR returned {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    null,
                    response.StatusCode);
            }

            return responseBytes;
        }

        throw new HttpRequestException("Mistral OCR retries were exhausted.");
    }

    private static (double? Width, double? Height) GetDimensions(JsonElement page)
    {
        if (page.TryGetProperty("dimensions", out JsonElement dimensions)
            && dimensions.ValueKind == JsonValueKind.Object
            && dimensions.TryGetProperty("width", out JsonElement width)
            && dimensions.TryGetProperty("height", out JsonElement height)
            && width.TryGetDouble(out double widthValue)
            && height.TryGetDouble(out double heightValue))
        {
            return (widthValue, heightValue);
        }

        return (null, null);
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static ReadOnlyMemory<byte>? DecodeImage(JsonElement image)
    {
        if (!image.TryGetProperty("image_base64", out JsonElement base64)
            || base64.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string raw = base64.GetString()!;
        int comma = raw.IndexOf(',');
        return Convert.FromBase64String(comma >= 0 ? raw[(comma + 1)..] : raw);
    }

    private static ExtractionDocumentSourceReference CreateImageSource(
        ExtractionDocumentSourcePage sourcePage,
        JsonElement image)
    {
        JsonElement native = image.Clone();
        if (!TryCoordinate(image, "top_left_x", out double left)
            || !TryCoordinate(image, "top_left_y", out double top)
            || !TryCoordinate(image, "bottom_right_x", out double right)
            || !TryCoordinate(image, "bottom_right_y", out double bottom))
        {
            return new ExtractionDocumentSourceReference(sourcePage, rawRepresentation: native);
        }

        return new ExtractionDocumentSourceReference(
            sourcePage,
            [new(left, top), new(right, top), new(right, bottom), new(left, bottom)],
            native);
    }

    private static bool TryCoordinate(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetDouble(out value);
    }

    internal static (int Start, int Length)? FindUnique(string source, string value)
    {
        int start = source.IndexOf(value, StringComparison.Ordinal);
        return start >= 0
            && source.IndexOf(value, start + value.Length, StringComparison.Ordinal) < 0
                ? (start, value.Length)
                : null;
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
