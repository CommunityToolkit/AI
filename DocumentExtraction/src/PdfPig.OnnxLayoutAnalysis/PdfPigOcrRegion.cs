// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>A detached actual detector region and CTC recognition observation, not a measured word or glyph.</summary>
public sealed class PdfPigOcrRegion
{
    internal PdfPigOcrRegion(int pageNumber, PageRaster raster, OcrQuad quad, RectifiedCrop crop, string text, float confidence)
    {
        PageNumber = pageNumber;
        Text = text;
        Confidence = confidence;
        DetectionConfidence = quad.Score;
        RasterWidth = raster.Width;
        RasterHeight = raster.Height;
        CropWidth = crop.Width;
        CropHeight = crop.Height;
        RasterPolygon = Array.AsReadOnly(quad.Points.Select(p => new PdfPoint(p.X, p.Y)).ToArray());
        Polygon = Array.AsReadOnly(quad.Points.Select(p =>
            new PdfPoint(p.X * raster.PageWidth / raster.Width, raster.PageHeight - p.Y * raster.PageHeight / raster.Height)).ToArray());
        Bounds = new(Polygon.Min(p => p.X), Polygon.Min(p => p.Y), Polygon.Max(p => p.X), Polygon.Max(p => p.Y));
        RasterToCropTransform = Array.AsReadOnly(crop.RasterToCrop.ToArray());
    }

    /// <summary>Gets the actual one-based PDF page number.</summary>
    public int PageNumber { get; }
    /// <summary>Gets the literal CTC result. Empty means no recognized text, not a certified blank region.</summary>
    public string Text { get; }
    /// <summary>Gets mean emitted token probability, or zero for an empty decode; this is not calibrated accuracy.</summary>
    public float Confidence { get; }
    /// <summary>Gets the detector's mean region probability.</summary>
    public float DetectionConfidence { get; }
    /// <summary>Gets the axis-aligned envelope of the actual source polygon in PDF display points.</summary>
    public PdfRectangle Bounds { get; }
    /// <summary>Gets ordered detector corners in the PDF display frame, with bottom-left origin.</summary>
    public IReadOnlyList<PdfPoint> Polygon { get; }
    /// <summary>Gets ordered detector corners in the original raster, with top-left origin.</summary>
    public IReadOnlyList<PdfPoint> RasterPolygon { get; }
    /// <summary>Gets the original raster width.</summary>
    public int RasterWidth { get; }
    /// <summary>Gets the original raster height.</summary>
    public int RasterHeight { get; }
    /// <summary>Gets the rectified crop width before recognizer resizing.</summary>
    public int CropWidth { get; }
    /// <summary>Gets the rectified crop height before recognizer resizing.</summary>
    public int CropHeight { get; }
    /// <summary>Gets the nine row-major homography values mapping original raster pixels to the rectified crop.</summary>
    public IReadOnlyList<double> RasterToCropTransform { get; }
}
