// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.Util;

namespace CommunityToolkit.DocumentExtraction.PdfPig;

/// <summary>Extracts native PDF text and source geometry. This provider does not perform OCR.</summary>
public sealed class PdfPigDocumentExtractionClient(
    IPageSegmenter? pageSegmenter = null,
    IWordExtractor? wordExtractor = null,
    Func<TextBlock, string?>? roleResolver = null) : IDocumentExtractionClient
{
    private const string ProviderName = "pdfpig";

    /// <inheritdoc/>
    public async Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType,
        DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("PdfPig supports application/pdf only.");
        }

        DocumentExtractionOptions request = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(request, nameof(PdfPigDocumentExtractionClient));
        _ = ProviderAdapterHelpers.ResolveExtractor(request, ProviderName, nameof(PdfPigDocumentExtractionClient), false);
        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        using var pdfInput = new MemoryStream(buffer.ToArray(), writable: false);
        using PdfDocument pdf = PdfDocument.Open(pdfInput);
        var output = new ExtractionDocument(identifier);
        IPageSegmenter segmenter = pageSegmenter ?? DefaultPageSegmenter.Instance;
        for (int pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(pageNumber);
            (ExtractionDocumentSourcePage frame, _) = ProviderAdapterHelpers.CreatePageFrame(
                identifier, ProviderName, pageNumber, page.Width, page.Height,
                ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft);
            output.SourcePages.Add(frame);
            var section = new ExtractionDocumentSection();
            section.Sources.Add(new ExtractionDocumentSourceReference(frame));
            output.Sections.Add(section);
            foreach (var block in segmenter.GetBlocks(wordExtractor is null ? page.GetWords() : page.GetWords(wordExtractor)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bounds = block.BoundingBox;
                var source = new ExtractionDocumentSourceReference(frame,
                    [
                        new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom),
                        new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Top),
                    ], block);
                ProviderAdapterHelpers.AddElement(section,
                    new ExtractionDocumentTextElement(new TextContent(block.Text),
                        role: ProviderAdapterHelpers.NormalizeTextRole(roleResolver?.Invoke(block))), [source]);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return output;
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
    }
}
