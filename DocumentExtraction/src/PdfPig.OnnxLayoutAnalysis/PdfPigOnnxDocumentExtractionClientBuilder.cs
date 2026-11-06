// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Configures a fixed local PDF pipeline without loading models until <see cref="Build"/>.</summary>
/// <remarks>Options and callback results are copied. The builder is not thread-safe. It does not mutate built clients or reorder execution stages.</remarks>
public sealed class PdfPigOnnxDocumentExtractionClientBuilder
{
    private readonly OnnxLayoutOptions _options;

    /// <summary>Captures a recipe. Validation and local model loading occur at Build, not here.</summary>
    /// <param name="options">Root settings, including the required layout path. Supplied ORT SessionOptions remain borrowed until each Build completes.</param>
    public PdfPigOnnxDocumentExtractionClientBuilder(OnnxLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Copy();
    }

    /// <summary>Configures OCR, enabling it when the resulting recipe is built.</summary>
    public PdfPigOnnxDocumentExtractionClientBuilder WithOcr(Action<OnnxOcrOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = _options.Ocr?.Copy() ?? new();
        configure(options);
        _options.Ocr = options.Copy();
        return this;
    }

    /// <summary>Configures table structure, enabling it when the resulting recipe is built.</summary>
    public PdfPigOnnxDocumentExtractionClientBuilder WithTableStructure(Action<OnnxTableStructureOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = _options.TableStructure?.Copy() ?? new();
        configure(options);
        _options.TableStructure = options.Copy();
        return this;
    }

    /// <summary>Enables the left-to-right column-aware geometric reading order heuristic.</summary>
    public PdfPigOnnxDocumentExtractionClientBuilder WithColumnReadingOrder()
    {
        _options.UseColumnReadingOrder = true;
        return this;
    }

    /// <summary>Validates the captured recipe and eagerly loads its local models into a new independently owned client.</summary>
    public PdfPigOnnxDocumentExtractionClient Build() => new(_options);
}
