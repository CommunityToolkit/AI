using Microsoft.ML.Tokenizers;

namespace CommunityToolkit.DataRetrieval.OnnxReranker.Internal;

/// <summary>
/// Batch of tokenized text pairs for ONNX cross-encoder inference.
/// </summary>
internal sealed class TokenizedBatch
{
    public long[][] TokenIds { get; }
    public long[][] AttentionMasks { get; }
    public long[][] TokenTypeIds { get; }
    public int SequenceLength { get; }
    public int Count => TokenIds.Length;

    public TokenizedBatch(long[][] tokenIds, long[][] attentionMasks, long[][] tokenTypeIds, int sequenceLength)
    {
        TokenIds = tokenIds;
        AttentionMasks = attentionMasks;
        TokenTypeIds = tokenTypeIds;
        SequenceLength = sequenceLength;
    }
}

/// <summary>
/// Tokenizes query-document pairs for cross-encoder models.
/// Produces [CLS] query_tokens [SEP] document_tokens [SEP] with proper token_type_ids.
/// </summary>
internal sealed class TextPairTokenizer
{
    private readonly Tokenizer _tokenizer;
    private readonly int _maxTokenLength;
    private readonly int _bosTokenId;
    private readonly int _sepTokenId;
    private readonly bool _doubleSeparator;

    public TextPairTokenizer(Tokenizer tokenizer, int maxTokenLength, int bosTokenId, int sepTokenId, bool doubleSeparator = false)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _maxTokenLength = maxTokenLength;
        _bosTokenId = bosTokenId;
        _sepTokenId = sepTokenId;
        _doubleSeparator = doubleSeparator;
    }

    /// <summary>
    /// Tokenizes a batch of query-document pairs for cross-encoder scoring.
    /// </summary>
    public TokenizedBatch TokenizePairs(IReadOnlyList<string> queries, IReadOnlyList<string> documents)
    {
        if (queries.Count != documents.Count)
            throw new ArgumentException("queries and documents must have the same length.");

        int batchSize = queries.Count;
        var allTokenIds = new long[batchSize][];
        var allAttentionMasks = new long[batchSize][];
        var allTokenTypeIds = new long[batchSize][];

        for (int i = 0; i < batchSize; i++)
        {
            var tokenIds = new long[_maxTokenLength];
            var attentionMask = new long[_maxTokenLength];
            var tokenTypeIds = new long[_maxTokenLength];

            TokenizeSinglePair(queries[i], documents[i], tokenIds, attentionMask, tokenTypeIds);

            allTokenIds[i] = tokenIds;
            allAttentionMasks[i] = attentionMask;
            allTokenTypeIds[i] = tokenTypeIds;
        }

        return new TokenizedBatch(allTokenIds, allAttentionMasks, allTokenTypeIds, _maxTokenLength);
    }

    private void TokenizeSinglePair(
        string textA, string textB,
        long[] tokenIds, long[] attentionMask, long[] tokenTypeIds)
    {
        var encodedA = _tokenizer.EncodeToTokens(textA, out _);
        var encodedB = _tokenizer.EncodeToTokens(textB, out _);

        var combined = new List<int>(_maxTokenLength);
        combined.Add(_bosTokenId);

        for (int j = 0; j < encodedA.Count; j++)
            combined.Add(encodedA[j].Id);

        combined.Add(_sepTokenId);

        if (_doubleSeparator)
            combined.Add(_sepTokenId);

        int firstSepIdx = combined.Count - 1;

        for (int j = 0; j < encodedB.Count; j++)
            combined.Add(encodedB[j].Id);

        combined.Add(_sepTokenId);

        if (combined.Count > _maxTokenLength)
            combined.RemoveRange(_maxTokenLength, combined.Count - _maxTokenLength);

        for (int s = 0; s < combined.Count; s++)
        {
            tokenIds[s] = combined[s];
            attentionMask[s] = 1;
            tokenTypeIds[s] = s <= firstSepIdx ? 0 : 1;
        }
    }
}
