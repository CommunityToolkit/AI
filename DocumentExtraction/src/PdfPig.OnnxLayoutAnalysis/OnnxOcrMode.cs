// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Chooses one text authority for each PDF page; recognition never implicitly merges with native text.</summary>
public enum OnnxOcrMode
{
    /// <summary>Recognizes a whole page only when it has no visible native words. Images on other pages are not recognized.</summary>
    PagesWithoutNativeText = 1,

    /// <summary>Recognizes every whole page instead of emitting native text. Native observations remain available separately.</summary>
    WholePage,
}
