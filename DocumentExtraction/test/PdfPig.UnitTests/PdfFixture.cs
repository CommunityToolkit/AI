using System.Globalization;
using System.Text;

namespace CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;

internal static class PdfFixture
{
    // Minimal local PDF: native text, a blank page, and a red image-only page.
    public static byte[] Create(bool empty = false)
    {
        string[] objects = empty
        ? [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
        ]
        :
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R] /Count 3 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << /Font << /F1 9 0 R >> >> /Contents 4 0 R >>",
            StreamObject("BT /F1 18 Tf 72 328 Td (Native invoice 42) Tj ET"),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 600] /Resources << >> /Contents 6 0 R >>",
            StreamObject(""),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 700 800] /Resources << /XObject << /Im1 10 0 R >> >> /Contents 8 0 R >>",
            StreamObject("q 72 0 0 72 24 24 cm /Im1 Do Q"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length 7 >>\nstream\nFF0000>\nendstream",
        ];
        return Write(objects);
    }

    private static string StreamObject(string body) => $"<< /Length {body.Length} >>\nstream\n{body}\nendstream";

    // Single 612x792 page with Helvetica as /F1 and the given content stream operators.
    public static byte[] CreatePage(IEnumerable<string> operations)
    {
        string body = string.Join("\n", operations);
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            StreamObject(body),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        return Write(objects);
    }

    public static string Text(double x, double y, string text, double size = 10) =>
        string.Create(CultureInfo.InvariantCulture, $"BT /F1 {size} Tf {x} {y} Td ({text}) Tj ET");

    public static string Line(double x1, double y1, double x2, double y2) =>
        string.Create(CultureInfo.InvariantCulture, $"0.5 w {x1} {y1} m {x2} {y2} l S");

    public static IEnumerable<string> Box(double left, double bottom, double right, double top) =>
    [
        Line(left, bottom, right, bottom), Line(right, bottom, right, top),
        Line(right, top, left, top), Line(left, top, left, bottom),
    ];

    // Draws bordered cells on a grid whose columns start at left and rows start at top, with text near each cell's top-left.
    public static IEnumerable<string> Grid(
        double left, double top, double columnWidth, double rowHeight,
        params (int Row, int Column, int RowSpan, int ColumnSpan, string Text)[] cells)
    {
        foreach (var cell in cells)
        {
            double cellLeft = left + (cell.Column * columnWidth);
            double cellTop = top - (cell.Row * rowHeight);
            double cellRight = cellLeft + (cell.ColumnSpan * columnWidth);
            double cellBottom = cellTop - (cell.RowSpan * rowHeight);
            foreach (string operation in Box(cellLeft, cellBottom, cellRight, cellTop))
            {
                yield return operation;
            }

            if (cell.Text.Length > 0)
            {
                yield return Text(cellLeft + 4, cellTop - 14, cell.Text);
            }
        }
    }

    private static byte[] Write(string[] objects)
    {
        var result = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach ((string value, int index) in objects.Select((value, index) => (value, index)))
        {
            offsets.Add(result.Length);
            result.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{value}\nendobj\n");
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
