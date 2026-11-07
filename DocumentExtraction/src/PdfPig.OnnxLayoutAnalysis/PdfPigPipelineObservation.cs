// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using UglyToad.PdfPig.Content;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Detached native and optional-model observations. Present only when an enrichment capability is enabled.</summary>
public sealed class PdfPigPipelineObservation
{
    internal PdfPigPipelineObservation(
        IEnumerable<PdfPigLayoutRegion> nativeRegions,
        IDictionary<int, IReadOnlyList<Word>> nativePageWords,
        IEnumerable<PdfPigOcrRegion> ocrRegions, IEnumerable<PdfPigTableStructure>? tableStructures = null,
        IEnumerable<PdfPigOcrProbabilityAdjustment>? probabilityAdjustments = null)
    {
        NativeRegions = Array.AsReadOnly(nativeRegions.ToArray());
        NativePageWords = new ReadOnlyDictionary<int, IReadOnlyList<Word>>(
            nativePageWords.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Word>)Array.AsReadOnly(pair.Value.ToArray())));
        OcrRegions = Array.AsReadOnly(ocrRegions.ToArray());
        TableStructures = Array.AsReadOnly((tableStructures ?? []).ToArray());
        OcrProbabilityAdjustments = Array.AsReadOnly((probabilityAdjustments ?? []).ToArray());
    }

    /// <summary>Gets the original native Heron region observations, including genuine PdfPig words even when OCR is authoritative.</summary>
    public IReadOnlyList<PdfPigLayoutRegion> NativeRegions { get; }
    /// <summary>Gets original native words by actual PDF page number, including native words outside detected regions.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Word>> NativePageWords { get; }
    /// <summary>Gets actual OCR observations, including empty decodes, without interpreting them as native words or certified blanks.</summary>
    public IReadOnlyList<PdfPigOcrRegion> OcrRegions { get; }
    /// <summary>Gets actual table proposals/topologies, including regions with no accepted rows or columns.</summary>
    public IReadOnlyList<PdfPigTableStructure> TableStructures { get; }
    /// <summary>Gets explicit raw-value evidence for any bounded float32 OCR probability saturation adjustments.</summary>
    public IReadOnlyList<PdfPigOcrProbabilityAdjustment> OcrProbabilityAdjustments { get; }
}
