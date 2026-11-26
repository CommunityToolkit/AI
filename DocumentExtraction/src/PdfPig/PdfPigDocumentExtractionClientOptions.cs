// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.Util;

namespace CommunityToolkit.DocumentExtraction.PdfPig;

/// <summary>Configures native PDF text, layout and table extraction.</summary>
/// <remarks>Values are read when the client is created; later changes have no effect on that client.</remarks>
public sealed class PdfPigDocumentExtractionClientOptions
{
    /// <summary>Gets or sets the segmenter that groups words into text blocks. Defaults to PdfPig's <c>DefaultPageSegmenter</c>.</summary>
    /// <remarks>The client borrows the segmenter and does not dispose it.</remarks>
    public IPageSegmenter? PageSegmenter { get; set; }

    /// <summary>Gets or sets the word extractor. Defaults to PdfPig's built-in word extraction.</summary>
    /// <remarks>
    /// The client borrows the extractor and does not dispose it. Its words form both text blocks and table cell text;
    /// each word appears once, either in a table cell or in a text block.
    /// </remarks>
    public IWordExtractor? WordExtractor { get; set; }

    /// <summary>Gets or sets a callback that assigns a role, such as <c>heading</c>, to a text block.</summary>
    public Func<TextBlock, string?>? RoleResolver { get; set; }

    /// <summary>Gets or sets how tables are detected. Defaults to <see cref="PdfPigTableExtractionMode.Lattice"/>.</summary>
    public PdfPigTableExtractionMode TableExtraction { get; set; } = PdfPigTableExtractionMode.Lattice;
}
