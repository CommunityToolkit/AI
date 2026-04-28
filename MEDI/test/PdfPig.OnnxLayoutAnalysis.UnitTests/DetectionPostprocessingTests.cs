#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis.UnitTests;
using UglyToad.PdfPig.Core;
using CommunityToolkit.DocumentProcessing.PdfPig.OnnxLayoutAnalysis;
using Xunit;

public class DetectionPostprocessingTests
{
    #region CxCyWhToRect

    [Fact]
    public void CxCyWhToRect_CenteredBox()
    {
        // Center at (320, 240) in image coords, 100×50 box on a 640×480 page
        var rect = DetectionPostprocessing.CxCyWhToRect(320, 240, 100, 50, 640, 480);

        // Image coords: x1=270, y1=215, x2=370, y2=265
        // PDF Y-flip: pdfBottom = 480 - 265 = 215, pdfTop = 480 - 215 = 265
        Assert.Equal(270, rect.Left, 1);
        Assert.Equal(370, rect.Right, 1);
        Assert.Equal(215, rect.Bottom, 1);
        Assert.Equal(265, rect.Top, 1);
    }

    [Fact]
    public void CxCyWhToRect_TopLeftCornerBox()
    {
        // Box centered at (25, 25) with size 50×50 on 100×100 page
        var rect = DetectionPostprocessing.CxCyWhToRect(25, 25, 50, 50, 100, 100);

        // Image coords: x1=0, y1=0, x2=50, y2=50
        // PDF Y-flip: pdfBottom = 100-50=50, pdfTop = 100-0=100
        Assert.Equal(0, rect.Left, 1);
        Assert.Equal(50, rect.Right, 1);
        Assert.Equal(50, rect.Bottom, 1);
        Assert.Equal(100, rect.Top, 1);
    }

    [Fact]
    public void CxCyWhToRect_BottomRightCorner()
    {
        // Box centered at (75, 75) with size 50×50 on 100×100 page
        var rect = DetectionPostprocessing.CxCyWhToRect(75, 75, 50, 50, 100, 100);

        // Image coords: x1=50, y1=50, x2=100, y2=100
        // PDF Y-flip: pdfBottom = 100-100=0, pdfTop = 100-50=50
        Assert.Equal(50, rect.Left, 1);
        Assert.Equal(100, rect.Right, 1);
        Assert.Equal(0, rect.Bottom, 1);
        Assert.Equal(50, rect.Top, 1);
    }

    #endregion

    #region XyxyToRect

    [Fact]
    public void XyxyToRect_FlipsYAxis()
    {
        // Image coords: top-left (10, 20), bottom-right (100, 80) on 200×300 page
        var rect = DetectionPostprocessing.XyxyToRect(10, 20, 100, 80, 200, 300);

        Assert.Equal(10, rect.Left, 1);
        Assert.Equal(100, rect.Right, 1);
        // PDF Y-flip: pdfBottom = 300 - 80 = 220, pdfTop = 300 - 20 = 280
        Assert.Equal(220, rect.Bottom, 1);
        Assert.Equal(280, rect.Top, 1);
    }

    [Fact]
    public void XyxyToRect_FullPage()
    {
        // Image covers full page 640×480
        var rect = DetectionPostprocessing.XyxyToRect(0, 0, 640, 480, 640, 480);

        Assert.Equal(0, rect.Left, 1);
        Assert.Equal(640, rect.Right, 1);
        Assert.Equal(0, rect.Bottom, 1);
        Assert.Equal(480, rect.Top, 1);
    }

    [Fact]
    public void XyxyToRect_SmallBox()
    {
        var rect = DetectionPostprocessing.XyxyToRect(0, 0, 10, 10, 1000, 1000);

        Assert.Equal(0, rect.Left, 1);
        Assert.Equal(10, rect.Right, 1);
        Assert.Equal(990, rect.Bottom, 1);
        Assert.Equal(1000, rect.Top, 1);
    }

    #endregion

    #region ComputeIoU

    [Fact]
    public void ComputeIoU_IdenticalBoxes_ReturnsOne()
    {
        var a = new PdfRectangle(0, 0, 100, 100);
        var b = new PdfRectangle(0, 0, 100, 100);

        float iou = DetectionPostprocessing.ComputeIoU(a, b);

        Assert.Equal(1f, iou, 4);
    }

    [Fact]
    public void ComputeIoU_NonOverlapping_ReturnsZero()
    {
        var a = new PdfRectangle(0, 0, 50, 50);
        var b = new PdfRectangle(100, 100, 200, 200);

        float iou = DetectionPostprocessing.ComputeIoU(a, b);

        Assert.Equal(0f, iou, 4);
    }

    [Fact]
    public void ComputeIoU_EdgeAdjacent_ReturnsZero()
    {
        // Two boxes sharing an edge (no overlap area)
        var a = new PdfRectangle(0, 0, 50, 50);
        var b = new PdfRectangle(50, 0, 100, 50);

        float iou = DetectionPostprocessing.ComputeIoU(a, b);

        Assert.Equal(0f, iou, 4);
    }

    [Fact]
    public void ComputeIoU_HalfOverlap()
    {
        // Two 100×100 boxes overlapping by 50 in X
        var a = new PdfRectangle(0, 0, 100, 100);
        var b = new PdfRectangle(50, 0, 150, 100);

        float iou = DetectionPostprocessing.ComputeIoU(a, b);

        // Intersection: 50×100 = 5000
        // Union: 10000 + 10000 - 5000 = 15000
        // IoU = 5000/15000 = 1/3
        Assert.Equal(1f / 3f, iou, 3);
    }

    [Fact]
    public void ComputeIoU_ContainedBox()
    {
        // Small box fully inside large box
        var large = new PdfRectangle(0, 0, 100, 100);
        var small = new PdfRectangle(25, 25, 75, 75);

        float iou = DetectionPostprocessing.ComputeIoU(large, small);

        // Intersection = 50×50 = 2500
        // Union = 10000 + 2500 - 2500 = 10000
        // IoU = 2500/10000 = 0.25
        Assert.Equal(0.25f, iou, 3);
    }

    [Fact]
    public void ComputeIoU_SymmetricProperty()
    {
        var a = new PdfRectangle(10, 10, 60, 80);
        var b = new PdfRectangle(30, 20, 90, 70);

        Assert.Equal(
            DetectionPostprocessing.ComputeIoU(a, b),
            DetectionPostprocessing.ComputeIoU(b, a),
            4);
    }

    #endregion

    #region ApplyNms

    [Fact]
    public void ApplyNms_EmptyList_ReturnsEmpty()
    {
        var detections = new List<LayoutDetection>();
        var result = DetectionPostprocessing.ApplyNms(detections);

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyNms_SingleDetection_ReturnsSame()
    {
        var detection = new LayoutDetection(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.9f);
        var detections = new List<LayoutDetection> { detection };

        var result = DetectionPostprocessing.ApplyNms(detections);

        Assert.Single(result);
        Assert.Equal(detection, result[0]);
    }

    [Fact]
    public void ApplyNms_NonOverlapping_AllPreserved()
    {
        var detections = new List<LayoutDetection>
        {
            new(new PdfRectangle(0, 0, 50, 50), "text", 0, 0.9f),
            new(new PdfRectangle(200, 200, 300, 300), "text", 0, 0.8f),
            new(new PdfRectangle(400, 400, 500, 500), "text", 0, 0.7f)
        };

        var result = DetectionPostprocessing.ApplyNms(detections);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ApplyNms_IdenticalBoxes_KeepsHighestConfidence()
    {
        var detections = new List<LayoutDetection>
        {
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.5f),
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.9f),
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.7f)
        };

        var result = DetectionPostprocessing.ApplyNms(detections, 0.45f);

        Assert.Single(result);
        Assert.Equal(0.9f, result[0].Confidence, 4);
    }

    [Fact]
    public void ApplyNms_HighOverlap_Suppressed()
    {
        // Two boxes with ~70% overlap
        var detections = new List<LayoutDetection>
        {
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.9f),
            new(new PdfRectangle(20, 20, 120, 120), "text", 0, 0.7f)
        };

        var result = DetectionPostprocessing.ApplyNms(detections, 0.3f);

        // IoU of these boxes is significant → lower-confidence one suppressed
        Assert.Single(result);
        Assert.Equal(0.9f, result[0].Confidence, 4);
    }

    [Fact]
    public void ApplyNms_HighThreshold_KeepsBoth()
    {
        // Same boxes, but with very high IoU threshold → both kept
        var detections = new List<LayoutDetection>
        {
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.9f),
            new(new PdfRectangle(20, 20, 120, 120), "text", 0, 0.7f)
        };

        var result = DetectionPostprocessing.ApplyNms(detections, 0.99f);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ApplyNms_NullDetections_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DetectionPostprocessing.ApplyNms(null!));
    }

    [Fact]
    public void ApplyNms_MixedOverlap_CorrectBehavior()
    {
        // Three detections: A and B overlap heavily, C is separate
        var detections = new List<LayoutDetection>
        {
            new(new PdfRectangle(0, 0, 100, 100), "text", 0, 0.9f),   // A
            new(new PdfRectangle(10, 10, 110, 110), "text", 0, 0.8f), // B (overlaps A)
            new(new PdfRectangle(500, 500, 600, 600), "text", 0, 0.7f) // C (no overlap)
        };

        var result = DetectionPostprocessing.ApplyNms(detections, 0.3f);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.9f, result[0].Confidence, 4);
        Assert.Equal(0.7f, result[1].Confidence, 4);
    }

    #endregion

    #region ScaleToPage

    [Fact]
    public void ScaleToPage_SimpleScaling()
    {
        var det = new LayoutDetection(new PdfRectangle(0, 0, 320, 240), "text", 0, 0.9f);
        var detections = new List<LayoutDetection> { det };

        var result = DetectionPostprocessing.ScaleToPage(detections, 640, 480, 1280, 960);

        // Scale 2x in both dimensions
        Assert.Equal(0, result[0].BoundingBox.Left, 1);
        Assert.Equal(640, result[0].BoundingBox.Right, 1);
        Assert.Equal(0, result[0].BoundingBox.Bottom, 1);
        Assert.Equal(480, result[0].BoundingBox.Top, 1);
    }

    [Fact]
    public void ScaleToPage_NullDetections_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DetectionPostprocessing.ScaleToPage(null!, 640, 640, 100, 100));
    }

    #endregion
}
#endif
