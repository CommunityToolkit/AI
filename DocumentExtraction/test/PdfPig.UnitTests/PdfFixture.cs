using System.Globalization;
using System.Text;

namespace CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;

internal static class PdfFixture
{
    // Minimal local PDF: native text, a blank page, and a red image-only page.
    public static byte[] Create()
    {
        string[] objects =
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

    private static string StreamObject(string body) => $"<< /Length {body.Length} >>\nstream\n{body}\nendstream";
}
