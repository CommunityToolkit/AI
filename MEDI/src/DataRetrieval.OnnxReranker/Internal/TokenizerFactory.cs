using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace CommunityToolkit.DataRetrieval.OnnxReranker.Internal;

/// <summary>
/// Loads a HuggingFace-compatible tokenizer from a directory or file path.
/// Supports BERT/WordPiece, SentencePiece, and BPE tokenizer formats.
/// </summary>
internal static class TokenizerFactory
{
    /// <summary>
    /// Loads a tokenizer from the given path. Accepts a directory (with tokenizer_config.json
    /// or known vocab files) or a direct path to a config/vocab file.
    /// </summary>
    public static Tokenizer LoadTokenizer(string path)
    {
        if (Directory.Exists(path))
            return LoadFromDirectory(path);

        var fileName = Path.GetFileName(path).ToLowerInvariant();
        if (fileName == "tokenizer_config.json")
            return LoadFromConfig(path);

        if (fileName == "tokenizer.json")
            return LoadFromHuggingFaceTokenizerJson(path);

        return LoadFromVocabFile(path);
    }

    /// <summary>
    /// Extracts BOS (CLS) and SEP token IDs from tokenizer_config.json.
    /// Also determines whether the model uses double separators (RoBERTa-family).
    /// </summary>
    public static (int? BosTokenId, int? SepTokenId, bool DoubleSeparator) DiscoverSpecialTokens(string tokenizerPath)
    {
        string? configPath = null;

        if (Directory.Exists(tokenizerPath))
        {
            var candidate = Path.Combine(tokenizerPath, "tokenizer_config.json");
            if (File.Exists(candidate))
                configPath = candidate;
        }
        else if (Path.GetFileName(tokenizerPath).Equals("tokenizer_config.json", StringComparison.OrdinalIgnoreCase))
        {
            configPath = tokenizerPath;
        }

        if (configPath == null)
            return (null, null, false);

        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int? bosId = null;
        int? sepId = null;

        var clsTokenStr = root.TryGetProperty("cls_token", out var cls) ? cls.GetString() : null;
        var sepTokenStr = root.TryGetProperty("sep_token", out var sep) ? sep.GetString() : null;

        if (clsTokenStr != null && sepTokenStr != null
            && root.TryGetProperty("added_tokens_decoder", out var decoder))
        {
            foreach (var entry in decoder.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out int tokenId)) continue;
                var content = entry.Value.TryGetProperty("content", out var c) ? c.GetString() : null;
                if (content == null) continue;

                if (content == clsTokenStr && bosId == null)
                    bosId = tokenId;
                if (content == sepTokenStr && sepId == null)
                    sepId = tokenId;
            }
        }

        if (bosId == null || sepId == null)
        {
            var tokenizerClass = root.TryGetProperty("tokenizer_class", out var tc) ? tc.GetString() ?? "" : "";
            if (tokenizerClass.EndsWith("Fast", StringComparison.Ordinal))
                tokenizerClass = tokenizerClass[..^4];

            (int? defaultBos, int? defaultSep) = tokenizerClass switch
            {
                "BertTokenizer" or "DistilBertTokenizer" => ((int?)101, (int?)102),
                "RobertaTokenizer" or "GPT2Tokenizer" => ((int?)0, (int?)2),
                "DebertaTokenizer" or "DebertaV2Tokenizer" => ((int?)1, (int?)2),
                "XLMRobertaTokenizer" => ((int?)0, (int?)2),
                _ => ((int?)null, (int?)null)
            };
            bosId ??= defaultBos;
            sepId ??= defaultSep;
        }

        var tokClass = root.TryGetProperty("tokenizer_class", out var t) ? t.GetString() ?? "" : "";
        if (tokClass.EndsWith("Fast", StringComparison.Ordinal))
            tokClass = tokClass[..^4];
        bool doubleSep = tokClass is "RobertaTokenizer" or "GPT2Tokenizer" or "XLMRobertaTokenizer";

        return (bosId, sepId, doubleSep);
    }

    private static Tokenizer LoadFromDirectory(string directory)
    {
        var configPath = Path.Combine(directory, "tokenizer_config.json");
        if (File.Exists(configPath))
            return LoadFromConfig(configPath);

        var vocabTxt = Path.Combine(directory, "vocab.txt");
        if (File.Exists(vocabTxt))
            return LoadFromVocabFile(vocabTxt);

        var spModel = Path.Combine(directory, "tokenizer.model");
        if (File.Exists(spModel))
            return LoadFromVocabFile(spModel);

        var spBpeModel = Path.Combine(directory, "sentencepiece.bpe.model");
        if (File.Exists(spBpeModel))
            return LoadFromVocabFile(spBpeModel);

        var spmModel = Path.Combine(directory, "spm.model");
        if (File.Exists(spmModel))
            return LoadFromVocabFile(spmModel);

        var tokenizerJson = Path.Combine(directory, "tokenizer.json");
        if (File.Exists(tokenizerJson))
            return LoadFromHuggingFaceTokenizerJson(tokenizerJson);

        throw new FileNotFoundException(
            $"No tokenizer_config.json or known vocab file found in '{directory}'. " +
            $"Expected one of: tokenizer_config.json, vocab.txt, tokenizer.model, sentencepiece.bpe.model, spm.model, tokenizer.json.");
    }

    private static Tokenizer LoadFromConfig(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath)
            ?? throw new ArgumentException($"Cannot determine directory for config: {configPath}");

        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tokenizerClass = root.TryGetProperty("tokenizer_class", out var tcls)
            ? tcls.GetString() ?? ""
            : "";

        if (tokenizerClass.EndsWith("Fast", StringComparison.Ordinal))
            tokenizerClass = tokenizerClass[..^4];

        return tokenizerClass switch
        {
            "BertTokenizer" => LoadBertFromConfig(directory, root),
            "DistilBertTokenizer" => LoadBertFromConfig(directory, root),
            "XLMRobertaTokenizer" => LoadSentencePieceFromDirectory(directory),
            "LlamaTokenizer" => LoadSentencePieceFromDirectory(directory),
            "CamembertTokenizer" => LoadSentencePieceFromDirectory(directory),
            "T5Tokenizer" => LoadSentencePieceFromDirectory(directory),
            "AlbertTokenizer" => LoadSentencePieceFromDirectory(directory),
            "DebertaTokenizer" => LoadSentencePieceFromDirectory(directory),
            "DebertaV2Tokenizer" => LoadSentencePieceFromDirectory(directory),
            "GPT2Tokenizer" => LoadBpeFromDirectory(directory),
            "RobertaTokenizer" => LoadBpeFromDirectory(directory),
            _ when !string.IsNullOrEmpty(tokenizerClass) => throw new NotSupportedException(
                $"Unsupported tokenizer_class '{tokenizerClass}' in {configPath}. " +
                $"Supported: BertTokenizer, DebertaV2Tokenizer, XLMRobertaTokenizer, LlamaTokenizer, GPT2Tokenizer, RobertaTokenizer. " +
                $"Use a tokenizer_config.json with a supported tokenizer_class."),
            _ => throw new InvalidOperationException(
                $"No tokenizer_class found in {configPath}. Cannot auto-detect tokenizer type.")
        };
    }

    private static Tokenizer LoadBertFromConfig(string directory, JsonElement config)
    {
        var vocabPath = Path.Combine(directory, "vocab.txt");
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException(
                $"BERT tokenizer requires vocab.txt in '{directory}'.");

        var lowerCase = config.TryGetProperty("do_lower_case", out var lc) && lc.GetBoolean();

        using var stream = File.OpenRead(vocabPath);
        return BertTokenizer.Create(stream, new BertOptions { LowerCaseBeforeTokenization = lowerCase });
    }

    private static Tokenizer LoadSentencePieceFromDirectory(string directory)
    {
        var candidates = new[] { "sentencepiece.bpe.model", "tokenizer.model", "spiece.model", "spm.model" };
        foreach (var candidate in candidates)
        {
            var spPath = Path.Combine(directory, candidate);
            if (File.Exists(spPath))
            {
                using var stream = File.OpenRead(spPath);
                return SentencePieceTokenizer.Create(stream);
            }
        }

        throw new FileNotFoundException(
            $"SentencePiece tokenizer requires one of [{string.Join(", ", candidates)}] in '{directory}'.");
    }

    private static Tokenizer LoadBpeFromDirectory(string directory)
    {
        var vocabJson = Path.Combine(directory, "vocab.json");
        var mergesPath = Path.Combine(directory, "merges.txt");

        if (!File.Exists(vocabJson))
            throw new FileNotFoundException($"BPE tokenizer requires vocab.json in '{directory}'.");
        if (!File.Exists(mergesPath))
            throw new FileNotFoundException($"BPE tokenizer requires merges.txt in '{directory}'.");

        using var vocabStream = File.OpenRead(vocabJson);
        using var mergesStream = File.OpenRead(mergesPath);
        return CodeGenTokenizer.Create(vocabStream, mergesStream);
    }

    private static Tokenizer LoadFromHuggingFaceTokenizerJson(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            var vocabTxt = Path.Combine(directory, "vocab.txt");
            if (File.Exists(vocabTxt))
                return LoadFromVocabFile(vocabTxt);
        }

        throw new NotSupportedException(
            $"tokenizer.json without a tokenizer_config.json is not supported for cross-encoder reranking. " +
            $"Please provide a directory with tokenizer_config.json and vocab.txt.");
    }

    private static Tokenizer LoadFromVocabFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        using var stream = File.OpenRead(path);
        return ext switch
        {
            ".txt" => BertTokenizer.Create(stream),
            ".model" => SentencePieceTokenizer.Create(stream),
            _ => throw new NotSupportedException(
                $"Unsupported vocab file extension '{ext}'. " +
                $"Use .txt for BERT/WordPiece, .model for SentencePiece, " +
                $"or point to a directory with tokenizer_config.json.")
        };
    }
}
