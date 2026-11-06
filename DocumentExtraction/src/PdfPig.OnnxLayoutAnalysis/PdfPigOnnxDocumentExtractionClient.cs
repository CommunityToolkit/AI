// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.ML.OnnxRuntime;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Tokens;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Extracts native PDF text with local Heron ONNX page-image layout detection.</summary>
/// <remarks>Optional local OCR is explicitly configured. Models are never downloaded. Caller streams and supplied sessions remain borrowed.</remarks>
public sealed class PdfPigOnnxDocumentExtractionClient : DocumentExtractionClient
{
    private readonly InferenceSession _session;
    private readonly bool _ownsSession;
    private readonly OnnxLayoutOptions _options;
    private readonly PaddleOcrModel? _ocr;
    private readonly TableStructureModel? _tables;
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>Loads a local Heron uint8 ONNX export and owns the created CPU inference session.</summary>
    /// <param name="options">The required layout path and optional capabilities, validated and copied during construction. No download is performed.</param>
    public PdfPigOnnxDocumentExtractionClient(OnnxLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Snapshot(requireLayoutModel: true);
        _session = LocalOnnxSession.Create(_options.LayoutModelPath!, _options.SessionOptions);
        try
        {
            HeronModel.Validate(_session);
            _ocr = CreateOcr();
            _tables = CreateTables();
        }
        catch
        {
            _ocr?.Dispose();
            _session.Dispose();
            throw;
        }

        _ownsSession = true;
    }

    /// <summary>Uses a borrowed inference session with the Heron uint8 tensor contract.</summary>
    /// <param name="session">The session; it must remain alive during extraction and is not disposed by this client.</param>
    /// <param name="options">Rendering and confidence settings, copied during construction.</param>
    public PdfPigOnnxDocumentExtractionClient(InferenceSession session, OnnxLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _options = (options ?? new()).Snapshot();
        if (_options.LayoutModelPath is not null)
        {
            throw new ArgumentException("LayoutModelPath must be null when supplying a borrowed layout session.", nameof(options));
        }

        HeronModel.Validate(session);
        _session = session;
        try
        {
            _ocr = CreateOcr();
            _tables = CreateTables();
        }
        catch
        {
            _ocr?.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public override async Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType,
        DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("ONNX PDF layout extraction accepts application/pdf only.");
        }

        var request = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(request, nameof(PdfPigOnnxDocumentExtractionClient));
        _ = ProviderAdapterHelpers.ResolveExtractor(request, "pdfpig-onnx-heron", nameof(PdfPigOnnxDocumentExtractionClient), false);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        byte[] bytes = buffer.ToArray();
        using var pdf = PdfPageRenderer.Open(bytes);
        var output = new ExtractionDocument(identifier);
        var observations = new List<PdfPigLayoutRegion>();
        var nativePageWords = new Dictionary<int, IReadOnlyList<Word>>();
        var ocrObservations = new List<PdfPigOcrRegion>();
        var probabilityAdjustments = new List<PdfPigOcrProbabilityAdjustment>();
        var tableObservations = new List<PdfPigTableStructure>();
        int remainingGridPositions = 65_536;
        for (int number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(number);
            if (page.Dictionary.TryGet(NameToken.Create("UserUnit"), out var unit) &&
                (unit is not NumericToken numeric || numeric.Double != 1))
            {
                throw new NotSupportedException("Only a literal default PDF UserUnit (1) is supported by the PdfPig display-coordinate contract.");
            }

            var raster = PageRaster.Render(pdf, page, _options.RenderDpi / 72d, _options.MaximumPagePixels, cancellationToken);
            IReadOnlyList<HeronDetection> detections;
            IReadOnlyList<PdfPigOcrRegion>? recognized = null;
            if (_ocr is not null || _tables is not null)
            {
                var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToArray();
                nativePageWords.Add(number, Array.AsReadOnly(words));
                bool hasVisibleNativeWords = words.Any(word =>
                {
                    var box = LayoutAssembler.AxisAligned(word.BoundingBox);
                    return !string.IsNullOrWhiteSpace(word.Text) && box.Width > 0 && box.Height > 0 &&
                        box.Centroid.X >= 0 && box.Centroid.X <= page.Width && box.Centroid.Y >= 0 && box.Centroid.Y <= page.Height;
                });
                if (_ocr is not null && (_options.Ocr!.Mode == OnnxOcrMode.WholePage || !hasVisibleNativeWords))
                {
                    lock (_sync)
                    {
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        try
                        {
                            recognized = _ocr.Extract(number, raster, cancellationToken, probabilityAdjustments);
                        }
                        catch (InvalidDataException error)
                        {
                            throw new InvalidDataException($"Page {number}: OCR output or processing is invalid. {error.Message}", error);
                        }
                    }
                }
            }

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                detections = HeronModel.Detect(_session, raster, _options.ConfidenceThreshold, cancellationToken,
                    _options.UseCorrectedLayoutCoordinates);
            }

            var nativeRegions = LayoutAssembler.AddPage(output, page, detections, cancellationToken, _options.UseColumnReadingOrder);
            observations.AddRange(nativeRegions);
            if (recognized is not null)
            {
                OcrPageAssembler.ReplaceNativeContent(output.Sections[output.Sections.Count - 1],
                    output.SourcePages[output.SourcePages.Count - 1], nativeRegions, recognized,
                    _options.UseColumnReadingOrder, cancellationToken);
                ocrObservations.AddRange(recognized);
            }

            if (_tables is not null)
            {
                var indices = nativeRegions.Select((region, index) => (region, index)).Where(item => item.region.Label == "table").ToArray();
                if (indices.Length > 32)
                {
                    throw new InvalidDataException($"Page {number} exceeds the 32 table-region inference budget.");
                }

                foreach (var (region, regionIndex) in indices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TableTextToken[] tokens = recognized is null
                        ? region.Words.Select(word => new TableTextToken(word.Text, LayoutAssembler.AxisAligned(word.BoundingBox), word)).ToArray()
                        : recognized.Where(line => OcrPageAssembler.Match(line.Bounds, nativeRegions) == regionIndex)
                            .Select(line => new TableTextToken(line.Text, line.Bounds, line)).ToArray();
                    PdfPigTableStructure structure;
                    lock (_sync)
                    {
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        try
                        {
                            structure = _tables.Analyze(raster, region, tokens, cancellationToken);
                        }
                        catch (InvalidDataException error)
                        {
                            throw new InvalidDataException($"Page {number}, table region {regionIndex}: {error.Message}", error);
                        }
                    }

                    remainingGridPositions = TablePageAssembler.ReserveGrid(structure, remainingGridPositions);
                    TablePageAssembler.Replace(output.Sections[output.Sections.Count - 1],
                        output.SourcePages[output.SourcePages.Count - 1], structure, cancellationToken);
                    tableObservations.Add(structure);
                }

                if (indices.Length > 0)
                {
                    TablePageAssembler.Order(output.Sections[output.Sections.Count - 1], _options.UseColumnReadingOrder, cancellationToken);
                }
            }
        }

        output.RawRepresentation = _ocr is null && _tables is null ? observations.AsReadOnly() :
            new PdfPigPipelineObservation(observations, nativePageWords, ocrObservations, tableObservations, probabilityAdjustments);
        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    private PaddleOcrModel? CreateOcr() => _options.Ocr is { } ocr ?
        new(ocr.DetectorModelPath!, ocr.RecognizerModelPath!, _options.SessionOptions) : null;

    private TableStructureModel? CreateTables() => _options.TableStructure is { } table ?
        new(table.ModelPath!, _options.SessionOptions) : null;

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return serviceKey is null && serviceType == typeof(InferenceSession)
                ? _session : base.GetService(serviceType, serviceKey);
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _tables?.Dispose();
                    _ocr?.Dispose();
                    if (_ownsSession)
                    {
                        _session.Dispose();
                    }
                }
            }
        }

        base.Dispose(disposing);
    }
}
