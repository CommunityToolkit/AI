// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class ReadingOrderIntegrationTests
{
    [Fact]
    public void NativeStreamDisagreementChangesOnlyElementOrder()
    {
        using var pdf = PdfDocument.Open(ReadingOrderPdfFixture.Create());
        var page = pdf.GetPage(1);
        Assert.Equal("HeadingRoneLoneRtwoLtwoTablebodyRthreeLthreeCaptionFootnote",
            string.Concat(page.Letters.Select(letter => letter.Value)));
        HeronDetection[] detections =
        [
            new("text", .71f, new(230, 340, 380, 370)),
            new("footnote", .72f, new(0, 40, 400, 70)),
            new("title", .73f, new(0, 490, 400, 520)),
            new("text", .74f, new(10, 440, 180, 470)),
            new("table", .75f, new(0, 290, 400, 320)),
            new("picture", .76f, new(10, 220, 180, 270)),
            new("text", .77f, new(230, 190, 380, 210)),
            new("caption", .78f, new(10, 170, 180, 215)),
            new("text", .79f, new(10, 340, 180, 370)),
            new("text", .8f, new(230, 440, 380, 470)),
        ];
        var baseline = new ExtractionDocument("same");
        var explicitOff = new ExtractionDocument("same");
        var enabled = new ExtractionDocument("same");
        var baselineRegions = LayoutAssembler.AddPage(baseline, page, detections, default);
        var offRegions = LayoutAssembler.AddPage(explicitOff, page, detections, default, false);
        var enabledRegions = LayoutAssembler.AddPage(enabled, page, detections, default, true);

        string[] expectedOff = ["Heading", "Lone", "Rone", "Ltwo", "Rtwo", "Tablebody", "<image>", "Lthree|Caption", "Rthree", "Footnote"];
        string[] expectedOn = ["Heading", "Lone", "Ltwo", "Rone", "Rtwo", "Tablebody", "<image>", "Lthree|Caption", "Rthree", "Footnote"];
        Assert.Equal(expectedOff, baseline.EnumerateContent().Select(Text));
        Assert.Equal(expectedOn, enabled.EnumerateContent().Select(Text));
        AssertDocumentsEquivalent(baseline, explicitOff, ordered: true);
        AssertDocumentsEquivalent(baseline, enabled, ordered: false);
        AssertRegionsEquivalent(detections, baselineRegions, offRegions);
        AssertRegionsEquivalent(detections, baselineRegions, enabledRegions);
        AssertObservationAttachments(enabled, enabledRegions);
        Assert.Equal(new[] { "heading", null, null, null, null, "caption", null, "footnote" },
            enabled.EnumerateContent().OfType<ExtractionDocumentTextElement>().Select(element => element.Role));
        Assert.IsType<ExtractionDocumentTableElement>(enabled.EnumerateContent().ElementAt(5));
        Assert.IsType<ExtractionDocumentImageElement>(enabled.EnumerateContent().ElementAt(6));
    }

    [Fact]
    public void UnmatchedNativeBlocksParticipateInColumnOrdering()
    {
        using var pdf = PdfDocument.Open(ReadingOrderPdfFixture.Create());
        var page = pdf.GetPage(1);
        var visible = VisibleWords(page);
        // Everything except Ltwo has its own detection; Ltwo must survive as a native TextBlock.
        var detections = visible.Where(word => word.Text != "Ltwo").Select(word =>
            new HeronDetection("text", .8f, LayoutAssembler.AxisAligned(word.BoundingBox))).Reverse().ToArray();
        var off = new ExtractionDocument("unmatched");
        var on = new ExtractionDocument("unmatched");
        var offRegions = LayoutAssembler.AddPage(off, page, detections, default);
        var onRegions = LayoutAssembler.AddPage(on, page, detections, default, true);

        Assert.Equal(new[] { "Heading", "Lone", "Rone", "Ltwo", "Rtwo", "Tablebody", "Lthree", "Rthree", "Caption", "Footnote" },
            off.EnumerateContent().Select(Text));
        // Without full-width detection boxes, Heading/Tablebody are only word-width, not separators.
        Assert.Equal(new[] { "Heading", "Lone", "Ltwo", "Tablebody", "Lthree", "Caption", "Footnote", "Rone", "Rtwo", "Rthree" },
            on.EnumerateContent().Select(Text));
        var unmatched = Assert.Single(on.EnumerateContent(), element => Text(element) == "Ltwo");
        Assert.IsType<TextBlock>(unmatched.Sources.Single().GetRawRepresentation());
        AssertDocumentsEquivalent(off, on, ordered: false);
        AssertRegionsEquivalent(detections, offRegions, onRegions);
        AssertObservationAttachments(on, onRegions);
        Assert.Equal(visible.Select(word => word.Text).Order(StringComparer.Ordinal),
            on.EnumerateContent().Select(Text).Order(StringComparer.Ordinal));
    }

    public static IEnumerable<object[]> DisplayFrames()
    {
        foreach (int rotation in new[] { 0, 90, 180, 270 })
        {
            yield return [rotation, false];
            yield return [rotation, true];
        }
    }

    [Theory]
    [MemberData(nameof(DisplayFrames))]
    public void CropAndRotationPreserveNativeContentAndSourcePolygons(int rotation, bool crop)
    {
        using var originalPdf = PdfDocument.Open(ReadingOrderPdfFixture.Create());
        var originalWords = VisibleWords(originalPdf.GetPage(1)).ToDictionary(word => word.Text);
        using var pdf = PdfDocument.Open(ReadingOrderPdfFixture.Create(rotation, crop));
        var page = pdf.GetPage(1);
        var words = VisibleWords(page);
        Assert.Equal(originalWords.Keys.Order(StringComparer.Ordinal), words.Select(word => word.Text).Order(StringComparer.Ordinal));
        var detections = words.Select(word => new HeronDetection("text", .875f,
            LayoutAssembler.AxisAligned(word.BoundingBox))).Reverse().ToArray();
        var off = new ExtractionDocument("frame");
        var on = new ExtractionDocument("frame");
        var offRegions = LayoutAssembler.AddPage(off, page, detections, default, false);
        var onRegions = LayoutAssembler.AddPage(on, page, detections, default, true);

        AssertDocumentsEquivalent(off, on, ordered: false);
        AssertRegionsEquivalent(detections, offRegions, onRegions);
        AssertObservationAttachments(on, onRegions);
        var frame = Assert.Single(on.SourcePages);
        Assert.Equal(rotation is 90 or 270 ? 550 : 400, frame.Dimensions!.Value.Width);
        Assert.Equal(rotation is 90 or 270 ? 400 : 550, frame.Dimensions.Value.Height);
        Assert.Equal(1, frame.PageNumber);
        Assert.Equal(ExtractionDocumentCoordinateUnit.Point, frame.CoordinateUnit);
        Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, frame.CoordinateOrigin);
        foreach (var element in on.EnumerateContent())
        {
            string text = Text(element);
            var source = Assert.Single(element.Sources);
            Assert.Same(frame, source.Page);
            var native = Assert.IsType<PdfPigLayoutRegion>(source.GetRawRepresentation());
            Assert.Equal(text, Assert.Single(native.Words).Text);
            var nativeWord = words.Single(word => word.Text == text);
            Assert.Equal(LayoutAssembler.AxisAligned(nativeWord.BoundingBox), native.Bounds);
            // Word envelopes use orientation-dependent font metrics; the physical baseline
            // point, unlike a recomputed word envelope, transforms exactly.
            var expectedAnchor = Transform(originalWords[text].Letters[0].StartBaseLine, rotation);
            var actualAnchor = nativeWord.Letters[0].StartBaseLine;
            Assert.InRange(Math.Abs(expectedAnchor.X - actualAnchor.X), 0, .001);
            Assert.InRange(Math.Abs(expectedAnchor.Y - actualAnchor.Y), 0, .001);
            Assert.Equal(new[] { (native.Bounds.Left, native.Bounds.Bottom), (native.Bounds.Right, native.Bounds.Bottom),
                (native.Bounds.Right, native.Bounds.Top), (native.Bounds.Left, native.Bounds.Top) },
                source.Polygon.Select(point => (point.X, point.Y)));
        }

        Assert.Equal(words.Length, on.EnumerateContent().Count());
        Assert.DoesNotContain(on.EnumerateContent(), element => Text(element).Contains("OUTSIDE_CROP", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionsDefaultOffAndSnapshotsAreMutationIsolated(bool flag)
    {
        Assert.False(new OnnxLayoutOptions().UseColumnReadingOrder);
        Assert.False(new OnnxLayoutOptions().Snapshot().UseColumnReadingOrder);
        var options = new OnnxLayoutOptions
        {
            UseColumnReadingOrder = flag, ConfidenceThreshold = .375f, RenderDpi = 96, MaximumPagePixels = 123456,
        };
        var snapshot = options.Snapshot();
        options.UseColumnReadingOrder = !flag;
        options.ConfidenceThreshold = .9f;
        options.RenderDpi = 300;
        options.MaximumPagePixels = 7;
        Assert.NotSame(options, snapshot);
        Assert.Equal(flag, snapshot.UseColumnReadingOrder);
        Assert.Equal(.375f, snapshot.ConfidenceThreshold);
        Assert.Equal(96, snapshot.RenderDpi);
        Assert.Equal(123456, snapshot.MaximumPagePixels);
        options.UseColumnReadingOrder = flag;
        snapshot.UseColumnReadingOrder = !flag;
        snapshot.ConfidenceThreshold = .125f;
        Assert.Equal(flag, options.UseColumnReadingOrder);
        Assert.Equal(.9f, options.ConfidenceThreshold);
    }

    private static Word[] VisibleWords(Page page) => page.GetWords(NearestNeighbourWordExtractor.Instance)
        .Where(word => !string.IsNullOrWhiteSpace(word.Text) &&
            word.BoundingBox.Centroid.X >= 0 && word.BoundingBox.Centroid.X <= page.Width &&
            word.BoundingBox.Centroid.Y >= 0 && word.BoundingBox.Centroid.Y <= page.Height).ToArray();

    internal static string Text(ExtractionDocumentElement element) => element is ExtractionDocumentImageElement image
        ? image.Text ?? "<image>"
        : string.Join("|", Assert.IsType<TextContent>(element.Content).Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void AssertDocumentsEquivalent(ExtractionDocument expected, ExtractionDocument actual, bool ordered)
    {
        var left = expected.EnumerateContent().ToArray();
        var right = actual.EnumerateContent().ToArray();
        if (!ordered)
        {
            left = left.OrderBy(Text, StringComparer.Ordinal).ToArray();
            right = right.OrderBy(Text, StringComparer.Ordinal).ToArray();
        }

        Assert.Equal(left.Length, right.Length);
        Assert.Single(actual.Sections);
        var expectedFrame = Assert.Single(expected.SourcePages);
        var actualFrame = Assert.Single(actual.SourcePages);
        Assert.Equal(expectedFrame.Dimensions, actualFrame.Dimensions);
        Assert.Equal(expectedFrame.PageNumber, actualFrame.PageNumber);
        Assert.Equal(expectedFrame.CoordinateOrigin, actualFrame.CoordinateOrigin);
        Assert.Equal(expectedFrame.CoordinateUnit, actualFrame.CoordinateUnit);
        Assert.Same(actualFrame, Assert.Single(actual.Sections[0].Sources).Page);
        for (int i = 0; i < left.Length; i++)
        {
            Assert.Equal(left[i].GetType(), right[i].GetType());
            Assert.Equal(Text(left[i]), Text(right[i]));
            Assert.Equal((left[i].Content as TextContent)?.Text, (right[i].Content as TextContent)?.Text);
            Assert.Equal((left[i] as ExtractionDocumentTextElement)?.Role, (right[i] as ExtractionDocumentTextElement)?.Role);
            if (left[i] is ExtractionDocumentTableElement table)
            {
                var other = Assert.IsType<ExtractionDocumentTableElement>(right[i]);
                Assert.Equal(table.Cells.RowCount, other.Cells.RowCount);
                Assert.Equal(table.Cells.ColumnCount, other.Cells.ColumnCount);
            }

            var a = Assert.Single(left[i].Sources);
            var b = Assert.Single(right[i].Sources);
            Assert.Same(actualFrame, b.Page);
            Assert.Equal(a.Polygon.Select(point => (point.X, point.Y)), b.Polygon.Select(point => (point.X, point.Y)));
            if (a.GetRawRepresentation() is PdfPigLayoutRegion region)
            {
                var other = Assert.IsType<PdfPigLayoutRegion>(b.GetRawRepresentation());
                Assert.Equal(region.Bounds, other.Bounds);
                Assert.Equal(region.Label, other.Label);
                Assert.Equal(region.Confidence, other.Confidence);
                Assert.Equal(region.PageNumber, other.PageNumber);
                AssertWordsEquivalent(region.Words, other.Words);
            }
            else
            {
                var block = Assert.IsType<TextBlock>(a.GetRawRepresentation());
                var other = Assert.IsType<TextBlock>(b.GetRawRepresentation());
                Assert.Equal(block.BoundingBox, other.BoundingBox);
                Assert.Equal(block.Text, other.Text);
                AssertWordsEquivalent(block.TextLines.SelectMany(line => line.Words).ToArray(),
                    other.TextLines.SelectMany(line => line.Words).ToArray());
            }
        }
    }

    private static void AssertObservationAttachments(ExtractionDocument document, IReadOnlyList<PdfPigLayoutRegion> regions)
    {
        var sources = document.EnumerateContent().SelectMany(element => element.Sources).ToArray();
        foreach (var region in regions)
        {
            Assert.Same(region, Assert.Single(sources, source => ReferenceEquals(source.GetRawRepresentation(), region))
                .GetRawRepresentation());
        }
    }

    private static void AssertRegionsEquivalent(IReadOnlyList<HeronDetection> detections,
        IReadOnlyList<PdfPigLayoutRegion> expected, IReadOnlyList<PdfPigLayoutRegion> actual)
    {
        Assert.Equal(detections.Count, actual.Count);
        for (int i = 0; i < detections.Count; i++)
        {
            Assert.Equal(detections[i].Label, actual[i].Label);
            Assert.Equal(detections[i].Confidence, actual[i].Confidence);
            Assert.Equal(detections[i].Bounds, actual[i].Bounds);
            Assert.Equal(expected[i].PageNumber, actual[i].PageNumber);
            AssertWordsEquivalent(expected[i].Words, actual[i].Words);
        }
    }

    private static void AssertWordsEquivalent(IReadOnlyList<Word> expected, IReadOnlyList<Word> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            // GetWords produces new Word wrappers on each assembler invocation. Compare
            // exact native values and the underlying real page Letter objects instead.
            Assert.Equal(expected[i].Text, actual[i].Text);
            Assert.Equal(expected[i].BoundingBox, actual[i].BoundingBox);
            Assert.Equal(expected[i].Letters, actual[i].Letters);
        }
    }

    private static PdfPoint Transform(PdfPoint point, int rotation) => rotation switch
    {
        0 => point,
        90 => new(point.Y, 400 - point.X),
        180 => new(400 - point.X, 550 - point.Y),
        270 => new(550 - point.Y, point.X),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
    };
}
