namespace CommunityToolkit.DataRetrieval.OnnxReranker;

/// <summary>
/// Configuration for the ONNX cross-encoder reranker.
/// </summary>
public sealed class CrossEncoderRerankerOptions
{
    /// <summary>Path to the ONNX cross-encoder model file (.onnx).</summary>
    public required string ModelPath { get; set; }

    /// <summary>
    /// Path to tokenizer artifacts — either a directory containing tokenizer_config.json
    /// (or vocab.txt, tokenizer.model, etc.) or a direct path to a vocab file.
    /// </summary>
    public required string TokenizerPath { get; set; }

    /// <summary>Maximum token length for the combined query + document pair. Default: 512.</summary>
    public int MaxTokenLength { get; set; } = 512;

    /// <summary>Batch size for ONNX inference. Default: 32.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Optional GPU device ID. Null = CPU only.
    /// Requires the consuming application to reference Microsoft.ML.OnnxRuntime.Gpu.
    /// </summary>
    public int? GpuDeviceId { get; set; }

    /// <summary>
    /// If true and GPU initialization fails, fall back to CPU instead of throwing.
    /// Default: false.
    /// </summary>
    public bool FallbackToCpu { get; set; }

    /// <summary>Maximum number of results to return after reranking. Default: 5.</summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>
    /// Preferred ONNX output tensor names, searched in order.
    /// Default: ["logits", "output"] — appropriate for cross-encoder models.
    /// </summary>
    public string[] PreferredOutputNames { get; set; } = ["logits", "output"];
}
