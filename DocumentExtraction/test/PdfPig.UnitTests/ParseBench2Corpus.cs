using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;

// Downloads the pinned ParseBench-2 pages described in Corpus/ParseBench-2/corpus.json and verifies their hashes.
// Set DOCUMENT_EXTRACTION_CORPUS_DIR to choose the cache directory.
public sealed class ParseBench2Corpus : IAsyncLifetime
{
    public const string DirectoryVariable = "DOCUMENT_EXTRACTION_CORPUS_DIR";
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    private readonly ConcurrentDictionary<(string, PdfPigTableExtractionMode), Lazy<Task<ExtractionDocument>>> _results = new();

    public static CorpusManifest Manifest { get; } = LoadManifest();

    public string Directory { get; } = Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } directory
        ? directory
        : Path.Combine(Path.GetTempPath(), "CommunityToolkit.AI", "corpus", "parsebench-2", Manifest.Revision);

    public static CorpusPage GetPage(string id) => Manifest.Pages.Single(page => page.Id == id);

    public string GetPath(CorpusPage page) => Path.Combine(Directory, page.Sha256 + ".pdf");

    public static Uri GetUri(CorpusPage page) => new(Manifest.UrlTemplate
        .Replace("{revision}", Manifest.Revision, StringComparison.Ordinal)
        .Replace("{path}", string.Join("/", page.Path.Split('/').Select(Uri.EscapeDataString)), StringComparison.Ordinal));

    public Task<ExtractionDocument> ExtractAsync(CorpusPage page, PdfPigTableExtractionMode mode) =>
        _results.GetOrAdd((page.Id, mode), _ => new(() => ExtractFreshAsync(page, mode))).Value;

    public async Task<ExtractionDocument> ExtractFreshAsync(CorpusPage page, PdfPigTableExtractionMode mode)
    {
        using var client = new PdfPigDocumentExtractionClient(new PdfPigDocumentExtractionClientOptions { TableExtraction = mode });
        using var input = new MemoryStream(await File.ReadAllBytesAsync(GetPath(page)));
        return await client.ExtractAsync(input, page.Id, "application/pdf");
    }

    public async ValueTask InitializeAsync()
    {
        System.IO.Directory.CreateDirectory(Directory);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CommunityToolkit.AI-tests");
        foreach (CorpusPage page in Manifest.Pages)
        {
            Uri uri = GetUri(page);
            try
            {
                await EnsureFileAsync(GetPath(page), page.Sha256, (path, token) => DownloadAsync(http, uri, path, token), CancellationToken.None);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Corpus page {page.Id} could not be obtained from {uri}: {exception.Message}", exception);
            }
        }
    }

    public ValueTask DisposeAsync() => default;

    // Writes to a temporary file and only moves it into place after its hash matches, so the cache never holds a bad file.
    internal static async Task EnsureFileAsync(
        string path, string expectedSha256, Func<string, CancellationToken, Task> download, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && await HashAsync(path, cancellationToken) == expectedSha256)
        {
            return;
        }

        string temporary = path + ".tmp";
        try
        {
            await download(temporary, cancellationToken);
            string actual = await HashAsync(temporary, cancellationToken);
            if (actual != expectedSha256)
            {
                throw new InvalidDataException($"SHA-256 {actual} does not match the pinned {expectedSha256}.");
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task DownloadAsync(HttpClient http, Uri uri, string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            bool transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (transient && attempt < RetryDelays.Length)
            {
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? RetryDelays[attempt], cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            await using FileStream file = File.Create(path);
            await response.Content.CopyToAsync(file, cancellationToken);
            return;
        }
    }

    private static CorpusManifest LoadManifest()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Corpus", "ParseBench-2", "corpus.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        return JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException($"{path} is empty.");
    }
}

public sealed record CorpusManifest(string Dataset, string Revision, string License, string UrlTemplate, CorpusPage[] Pages);

public sealed record CorpusPage(
    string Id,
    string Path,
    string Sha256,
    string Set,
    string Ruling,
    string NoneTextSha256,
    Dictionary<PdfPigTableExtractionMode, string[]> Tables,
    PdfPigTableExtractionMode? FactMode,
    CorpusFact[]? Facts,
    string? StreamJustification);

public sealed record CorpusFact(
    string Kind, string Anchor, string? Expected, string[]? Headers, int? RowSpan, int? ColumnSpan, string Source);
