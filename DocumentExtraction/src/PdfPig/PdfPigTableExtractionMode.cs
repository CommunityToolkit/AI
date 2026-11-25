// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig;

/// <summary>Selects how tables are detected from native PDF content with Tabula.</summary>
public enum PdfPigTableExtractionMode
{
    /// <summary>Does not detect tables. All words are returned as text blocks.</summary>
    None = 0,

    /// <summary>Detects tables drawn with ruling lines or cell borders.</summary>
    Lattice = 1,

    /// <summary>
    /// Detects ruled tables, then whitespace-aligned tables in regions without a ruled table.
    /// Whitespace detection can report charts and forms as tables and does not infer merged cells.
    /// </summary>
    LatticeThenStream = 2,
}
