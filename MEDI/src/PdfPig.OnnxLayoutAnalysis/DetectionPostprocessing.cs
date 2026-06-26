using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;

/// <summary>
/// Static utility methods for postprocessing ONNX detection model outputs.
/// </summary>
public static class DetectionPostprocessing
{
    /// <summary>
    /// Convert center-x, center-y, width, height (image coordinates, top-left origin)
    /// to a <see cref="PdfRectangle"/> (bottom-left origin, Y-up).
    /// </summary>
    /// <param name="cx">Center X in image coordinates.</param>
    /// <param name="cy">Center Y in image coordinates.</param>
    /// <param name="w">Width.</param>
    /// <param name="h">Height.</param>
    /// <param name="pageWidth">Page width for coordinate mapping.</param>
    /// <param name="pageHeight">Page height for coordinate mapping.</param>
    /// <returns>A PDF rectangle in PDF coordinate space.</returns>
    public static PdfRectangle CxCyWhToRect(float cx, float cy, float w, float h, float pageWidth, float pageHeight)
    {
        float x1 = cx - w / 2f;
        float y1 = cy - h / 2f;
        float x2 = cx + w / 2f;
        float y2 = cy + h / 2f;

        return XyxyToRect(x1, y1, x2, y2, pageWidth, pageHeight);
    }

    /// <summary>
    /// Convert x1, y1, x2, y2 (image coordinates, top-left origin, Y-down)
    /// to a <see cref="PdfRectangle"/> (bottom-left origin, Y-up).
    /// </summary>
    /// <param name="x1">Left X in image coordinates.</param>
    /// <param name="y1">Top Y in image coordinates.</param>
    /// <param name="x2">Right X in image coordinates.</param>
    /// <param name="y2">Bottom Y in image coordinates.</param>
    /// <param name="pageWidth">Page width for coordinate mapping.</param>
    /// <param name="pageHeight">Page height for coordinate mapping.</param>
    /// <returns>A PDF rectangle in PDF coordinate space.</returns>
    public static PdfRectangle XyxyToRect(float x1, float y1, float x2, float y2, float pageWidth, float pageHeight)
    {
        // Image Y-axis is top-down; PDF Y-axis is bottom-up.
        // Flip Y: pdfY = pageHeight - imageY
        double pdfLeft = x1;
        double pdfRight = x2;
        double pdfBottom = pageHeight - y2;
        double pdfTop = pageHeight - y1;

        return new PdfRectangle(pdfLeft, pdfBottom, pdfRight, pdfTop);
    }

    /// <summary>
    /// Apply Non-Maximum Suppression to remove overlapping detections.
    /// Detections are processed in descending confidence order; a detection
    /// is suppressed if its IoU with any already-kept detection exceeds the threshold.
    /// </summary>
    /// <param name="detections">Input detections.</param>
    /// <param name="iouThreshold">IoU threshold above which a detection is suppressed.</param>
    /// <returns>Filtered detections after NMS.</returns>
    public static IReadOnlyList<LayoutDetection> ApplyNms(IReadOnlyList<LayoutDetection> detections, float iouThreshold = 0.45f)
    {
        ArgumentNullException.ThrowIfNull(detections);

        if (detections.Count <= 1)
        {
            return detections;
        }

        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var kept = new List<LayoutDetection>();
        var suppressed = new bool[sorted.Count];

        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i])
            {
                continue;
            }

            kept.Add(sorted[i]);

            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (suppressed[j])
                {
                    continue;
                }

                if (ComputeIoU(sorted[i].BoundingBox, sorted[j].BoundingBox) > iouThreshold)
                {
                    suppressed[j] = true;
                }
            }
        }

        return kept;
    }

    /// <summary>
    /// Scale detection bounding boxes from model coordinate space to PDF page coordinate space.
    /// Handles optional letterbox padding removal.
    /// </summary>
    /// <param name="detections">Detections in model coordinates.</param>
    /// <param name="modelWidth">Model input width.</param>
    /// <param name="modelHeight">Model input height.</param>
    /// <param name="pageWidth">Target page width (PDF units).</param>
    /// <param name="pageHeight">Target page height (PDF units).</param>
    /// <param name="letterboxScale">Scale factor used during letterboxing, or null if not letterboxed.</param>
    /// <param name="padX">Horizontal padding offset from letterboxing.</param>
    /// <param name="padY">Vertical padding offset from letterboxing.</param>
    /// <returns>Detections with bounding boxes in page coordinate space.</returns>
    public static IReadOnlyList<LayoutDetection> ScaleToPage(
        IReadOnlyList<LayoutDetection> detections,
        int modelWidth,
        int modelHeight,
        double pageWidth,
        double pageHeight,
        float? letterboxScale = null,
        int padX = 0,
        int padY = 0)
    {
        ArgumentNullException.ThrowIfNull(detections);

        var result = new List<LayoutDetection>(detections.Count);

        foreach (var det in detections)
        {
            var box = det.BoundingBox;

            double left = box.Left;
            double right = box.Right;
            double bottom = box.Bottom;
            double top = box.Top;

            if (letterboxScale.HasValue)
            {
                float s = letterboxScale.Value;
                left = (left - padX) / s;
                right = (right - padX) / s;
                bottom = (bottom - padY) / s;
                top = (top - padY) / s;
            }

            // Scale from image pixel space to page space
            double scaleX = pageWidth / modelWidth;
            double scaleY = pageHeight / modelHeight;

            if (letterboxScale.HasValue)
            {
                // When letterboxed, coordinates were already unpadded/unscaled to original image size.
                // Now scale from original image size to page size.
                // The original image size = modelWidth / letterboxScale (approximately)
                // but we already divided by letterboxScale, so coords are in original image space.
                // We still need to map from original image dims to page dims.
                // Since the original image was rendered from the page, they share the same aspect ratio.
                scaleX = pageWidth / (modelWidth / letterboxScale.Value - padX * 2.0 / letterboxScale.Value);
                scaleY = pageHeight / (modelHeight / letterboxScale.Value - padY * 2.0 / letterboxScale.Value);
            }

            left *= scaleX;
            right *= scaleX;
            bottom *= scaleY;
            top *= scaleY;

            // Clamp to page bounds
            left = Math.Max(0, Math.Min(left, pageWidth));
            right = Math.Max(0, Math.Min(right, pageWidth));
            bottom = Math.Max(0, Math.Min(bottom, pageHeight));
            top = Math.Max(0, Math.Min(top, pageHeight));

            var newBox = new PdfRectangle(left, bottom, right, top);
            result.Add(det with { BoundingBox = newBox });
        }

        return result;
    }

    /// <summary>
    /// Compute the Intersection over Union (IoU) of two rectangles.
    /// </summary>
    /// <param name="a">First rectangle.</param>
    /// <param name="b">Second rectangle.</param>
    /// <returns>IoU value in [0, 1].</returns>
    public static float ComputeIoU(PdfRectangle a, PdfRectangle b)
    {
        double interLeft = Math.Max(a.Left, b.Left);
        double interRight = Math.Min(a.Right, b.Right);
        double interBottom = Math.Max(a.Bottom, b.Bottom);
        double interTop = Math.Min(a.Top, b.Top);

        if (interLeft >= interRight || interBottom >= interTop)
        {
            return 0f;
        }

        double interArea = (interRight - interLeft) * (interTop - interBottom);
        double areaA = a.Width * a.Height;
        double areaB = b.Width * b.Height;
        double unionArea = areaA + areaB - interArea;

        if (unionArea <= 0)
        {
            return 0f;
        }

        return (float)(interArea / unionArea);
    }
}
