// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.ML.OnnxRuntime;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Extracts native PDF text with local Heron ONNX page-image layout detection.</summary>
/// <remarks>This provider does not perform OCR or download models. Caller streams and supplied sessions remain borrowed.</remarks>
public sealed class PdfPigOnnxDocumentExtractionClient : DocumentExtractionClient
{
    private readonly InferenceSession _session;
    private readonly bool _ownsSession;
    private readonly OnnxLayoutOptions _options;
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>Loads a local Heron uint8 ONNX export and owns the created CPU inference session.</summary>
    /// <param name="modelPath">The explicit local model path. No download is performed.</param>
    /// <param name="options">Rendering and confidence settings, copied during construction.</param>
    public PdfPigOnnxDocumentExtractionClient(string modelPath, OnnxLayoutOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _options = (options ?? new()).Snapshot();
        using var sessionOptions = new SessionOptions();
        _session = new InferenceSession(modelPath, sessionOptions);
        try
        {
            HeronModel.Validate(_session);
        }
        catch
        {
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
        HeronModel.Validate(session);
        _session = session;
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
        using var pdf = PdfDocument.Open(bytes);
        using var rendered = DocLib.Instance.GetDocReader(bytes, new PageDimensions(_options.RenderDpi / 72d));
        var output = new ExtractionDocument(identifier);
        var observations = new List<PdfPigLayoutRegion>();
        for (int number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(number);
            if (page.Dictionary.TryGet(NameToken.Create("UserUnit"), out var unit) &&
                (unit is not NumericToken numeric || numeric.Double != 1))
            {
                throw new NotSupportedException("Only a literal default PDF UserUnit (1) is supported by the PdfPig display-coordinate contract.");
            }

            using var pageReader = rendered.GetPageReader(number - 1);
            var raster = PageRaster.Render(pageReader, page, _options.MaximumPagePixels, cancellationToken);
            IReadOnlyList<HeronDetection> detections;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                detections = HeronModel.Detect(_session, raster, _options.ConfidenceThreshold, cancellationToken);
            }

            observations.AddRange(LayoutAssembler.AddPage(output, page, detections, cancellationToken));
        }

        output.RawRepresentation = observations.AsReadOnly();
        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

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
