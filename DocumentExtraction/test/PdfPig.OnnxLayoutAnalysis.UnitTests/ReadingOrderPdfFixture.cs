// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

/// <summary>Original MIT-licensed synthetic PDF; intentionally right/left-interleaved stream order.</summary>
internal static class ReadingOrderPdfFixture
{
    internal static byte[] Create(int rotation = 0, bool crop = false)
    {
        int dx = crop ? 50 : 0, dy = crop ? 100 : 0;
        string box = crop ? "/MediaBox [0 0 500 700] /CropBox [50 100 450 650]" : "/MediaBox [0 0 400 550]";
        string content = FormattableString.Invariant($"""
            q 1 0 0 1 {dx} {dy} cm
            BT /F1 12 Tf 20 500 Td (Heading) Tj ET
            BT /F1 12 Tf 240 450 Td (Rone) Tj ET
            BT /F1 12 Tf 20 450 Td (Lone) Tj ET
            BT /F1 12 Tf 240 350 Td (Rtwo) Tj ET
            BT /F1 12 Tf 20 350 Td (Ltwo) Tj ET
            BT /F1 12 Tf 20 300 Td (Tablebody) Tj ET
            BT /F1 12 Tf 240 200 Td (Rthree) Tj ET
            BT /F1 12 Tf 20 200 Td (Lthree) Tj ET
            BT /F1 12 Tf 20 180 Td (Caption) Tj ET
            BT /F1 12 Tf 20 50 Td (Footnote) Tj ET
            Q
            """);
        if (crop)
        {
            content += "\nBT /F1 12 Tf 10 20 Td (OUTSIDE_CROP) Tj ET";
        }

        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Type /Page /Parent 2 0 R {box} /Rotate {rotation} /Resources << /Font << /F1 3 0 R >> >> /Contents 5 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        ];
        var result = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (string value in objects)
        {
            offsets.Add(result.Length);
            result.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{value}\nendobj\n");
        }

        int xref = result.Length;
        result.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            result.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }

        result.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(result.ToString());
    }
}
