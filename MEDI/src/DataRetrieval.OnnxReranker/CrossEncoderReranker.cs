using CommunityToolkit.DataRetrieval.OnnxReranker.Internal;
using Microsoft.Extensions.DataRetrieval;
using Microsoft.ML.Tokenizers;

namespace CommunityToolkit.DataRetrieval.OnnxReranker;

/// <summary>
/// ONNX cross-encoder reranker. Scores query-document pairs using a local cross-encoder
/// model (e.g., ms-marco-MiniLM-L-6-v2, BGE-reranker) for fast, high-quality reranking.
///
/// Pipeline: text-pair tokenization → ONNX inference → sigmoid normalization.
///
/// Dependencies: Microsoft.ML.OnnxRuntime.Managed + Microsoft.ML.Tokenizers (no Microsoft.ML).
/// </summary>
/// <remarks>
/// Lazy initialization: the ONNX session and tokenizer are loaded on first use.
/// Call <see cref="Dispose"/> to release native ONNX resources.
/// </remarks>
public class CrossEncoderReranker : RetrievalResultProcessor, IReranker, IDisposable
{
    private readonly CrossEncoderRerankerOptions _options;
    private readonly object _initLock = new();

    private OnnxModelSession? _session;
    private TextPairTokenizer? _pairTokenizer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossEncoderReranker"/> class.
    /// </summary>
    /// <param name="options">Reranker configuration options.</param>
    public CrossEncoderReranker(CrossEncoderRerankerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.ModelPath))
            throw new ArgumentException("ModelPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.TokenizerPath))
            throw new ArgumentException("TokenizerPath is required.", nameof(options));
    }

    /// <inheritdoc/>
    public override async Task<RetrievalResults> ProcessAsync(
        RetrievalResults results, RetrievalQuery query, CancellationToken cancellationToken = default)
    {
        if (results.Chunks.Count <= 1)
            return results;

        var reranked = await RerankAsync(
            query.Text,
            results.Chunks.ToList(),
            cancellationToken);

        results.Chunks = reranked.Take(_options.MaxResults).ToList();
        results.Metadata["reranked"] = true;
        results.Metadata["reranker"] = "CrossEncoder";
        results.Metadata["reranked_count"] = results.Chunks.Count;
        return results;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RetrievalChunk>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return Task.FromResult<IReadOnlyList<RetrievalChunk>>([]);

        return Task.Run(() =>
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            var documents = chunks.Select(r => r.Content).ToList();
            var scores = ScoreDocuments(query, documents);

            var reranked = chunks
                .Select((chunk, i) =>
                {
                    chunk.Score = scores[i];
                    return chunk;
                })
                .OrderByDescending(c => c.Score)
                .ToList();

            return (IReadOnlyList<RetrievalChunk>)reranked;
        }, cancellationToken);
    }

    private float[] ScoreDocuments(string query, IList<string> documents)
    {
        var allScores = new List<float>(documents.Count);
        int batchSize = _options.BatchSize;

        for (int start = 0; start < documents.Count; start += batchSize)
        {
            int count = Math.Min(batchSize, documents.Count - start);
            var batchQueries = Enumerable.Repeat(query, count).ToList();
            var batchDocs = documents.Skip(start).Take(count).ToList();

            var tokenized = _pairTokenizer!.TokenizePairs(batchQueries, batchDocs);
            var rawOutputs = _session!.Score(tokenized);

            for (int i = 0; i < rawOutputs.Length; i++)
            {
                float logit = rawOutputs[i][0];
                allScores.Add(Sigmoid(logit));
            }
        }

        return [.. allScores];
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session != null && _pairTokenizer != null)
            return;

        lock (_initLock)
        {
            if (_session != null && _pairTokenizer != null)
                return;

            var tokenizer = TokenizerFactory.LoadTokenizer(_options.TokenizerPath);
            var (bosId, sepId, doubleSep) = TokenizerFactory.DiscoverSpecialTokens(_options.TokenizerPath);

            if (bosId == null || sepId == null)
                throw new InvalidOperationException(
                    "Could not determine BOS/CLS and SEP token IDs for text-pair tokenization. " +
                    "Ensure the tokenizer directory contains a tokenizer_config.json with cls_token/sep_token " +
                    "or a recognized tokenizer_class.");

            _pairTokenizer = new TextPairTokenizer(
                tokenizer, _options.MaxTokenLength, bosId.Value, sepId.Value, doubleSep);

            _session = new OnnxModelSession(
                _options.ModelPath, _options.GpuDeviceId, _options.FallbackToCpu, _options.PreferredOutputNames);
        }
    }

    private static float Sigmoid(float x) => 1.0f / (1.0f + MathF.Exp(-x));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _session?.Dispose();
        GC.SuppressFinalize(this);
    }
}
