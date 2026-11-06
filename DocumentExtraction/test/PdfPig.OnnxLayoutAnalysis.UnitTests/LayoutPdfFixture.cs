// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using SkiaSharp;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

internal static class LayoutPdfFixture
{
    internal static byte[] Create(int rotation = 0, bool crop = false, int pages = 1, bool userUnit = false, bool transparency = false,
        bool jpeg = false)
    {
        string box = crop ? "/MediaBox [0 0 500 700] /CropBox [50 100 450 650]" : "/MediaBox [0 0 400 550]";
        int dx = crop ? 50 : 0, dy = crop ? 100 : 0;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{4 + i * 2} 0 R"))}] /Count {pages} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        for (int i = 0; i < pages; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R {box} /Rotate {rotation} {(userUnit ? "/UserUnit 2" : "")} /Resources << /Font << /F1 3 0 R >> /XObject << /Im1 {4 + pages * 2} 0 R >> /ExtGState << /GS1 << /Type /ExtGState /ca 0.5 >> >> >> /Contents {5 + i * 2} 0 R >>");
            string content = FormattableString.Invariant($"""
                q 1 0 0 1 {dx} {dy} cm
                1 0 0 rg 20 430 140 70 re f
                q 100 0 0 70 250 430 cm /Im1 Do Q
                0 0 0 rg
                BT /F1 24 Tf 30 380 Td (LOCAL LAYOUT) Tj ET
                BT /F1 12 Tf 30 340 Td (Native words remain searchable.) Tj ET
                BT /F1 12 Tf 30 320 Td (Layout detection uses the real page.) Tj ET
                BT /F1 12 Tf 30 260 Td (Unmatched text is retained.) Tj ET
                {(transparency ? "q /GS1 gs 0 1 0 rg 350 10 30 30 re f Q" : "")}
                Q
                """);
            if (crop)
            {
                content += "\nBT /F1 12 Tf 10 20 Td (OUTSIDE_CROP) Tj ET";
            }
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }
        string image = "0000FF>";
        if (jpeg)
        {
            using var bitmap = new SKBitmap(1, 1);
            bitmap.Erase(SKColors.Blue);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
            image = Convert.ToHexString(encoded.ToArray()) + ">";
        }

        string filter = jpeg ? "[/ASCIIHexDecode /DCTDecode]" : "/ASCIIHexDecode";
        objects.Add($"<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter {filter} /Length {image.Length} >>\nstream\n{image}\nendstream");

        var result = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var value in objects)
        {
            offsets.Add(result.Length);
            result.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{value}\nendobj\n");
        }

        int xref = result.Length;
        result.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            result.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }

        result.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(result.ToString());
    }
}
