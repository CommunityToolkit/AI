// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

internal static class SyntheticSmoke
{
    internal static async Task RunAsync(string? expectedFramework = null)
    {
        var assembly = typeof(PdfPigOnnxDocumentExtractionClient).Assembly;
        string? framework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        string? expectedAttribute = expectedFramework switch
        {
            null => null,
            "netstandard2.0" => ".NETStandard,Version=v2.0",
            "net10.0" => ".NETCoreApp,Version=v10.0",
            _ => throw new ArgumentException("Unexpected companion asset framework.", nameof(expectedFramework)),
        };
        if (expectedAttribute is not null && framework != expectedAttribute)
        {
            throw new InvalidDataException($"Expected actual {expectedFramework} assembly, loaded '{framework}'.");
        }
        string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location)));
        if (Environment.GetEnvironmentVariable("EXPECTED_COMPANION_ASSET_SHA256") is { } expected &&
            !string.Equals(hash, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Loaded companion assembly does not match the selected package asset.");
        }

        string directory = Path.Combine(Path.GetTempPath(), $"pdfpig-onnx-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "synthetic.onnx");
        try
        {
            await File.WriteAllBytesAsync(path, CreateGraph());
            using var client = new PdfPigOnnxDocumentExtractionClientBuilder(new()
            {
                LayoutModelPath = path,
                UseCorrectedLayoutCoordinates = true,
            }).WithColumnReadingOrder().Build();
            using var input = new MemoryStream(CreatePdf());
            var result = await client.ExtractAsync(input, "synthetic", "application/pdf");
            string text = string.Join(" ", result.EnumerateContent().Select(element => (element.Content as TextContent)?.Text));
            if (result.SourcePages.Count != 1 ||
                result.RawRepresentation is not IReadOnlyList<PdfPigLayoutRegion> { Count: 300 } ||
                !text.Contains("Synthetic native 42", StringComparison.Ordinal) || !input.CanRead)
            {
                throw new InvalidDataException("Synthetic graph/PDF contract extraction failed.");
            }

            Console.WriteLine($"Synthetic-only extraction passed; framework={framework}; assembly SHA256={hash}; 1 page, 300 contract regions, native literal retained.");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            Directory.Delete(directory);
        }
    }

    private static byte[] CreatePdf()
    {
        const string content = "BT /F1 16 Tf 72 328 Td (Synthetic native 42) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }
        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    // Tiny generated XYXY scaling graph, not curated weights or a recognition-quality fixture.
    private static byte[] CreateGraph() => Convert.FromBase64String(
        "CAg6yQUKPQoRb3JpZ190YXJnZXRfc2l6ZXMKB2luZGljZXMSCnh5eHlfc2l6ZXMiBkdhdGhlcioLCgRheGlzGAGgAQIKKgoKeHl4eV9zaXplcxILZmxvYXRfc2l6ZXMiBENhc3QqCQoCdG8YAaABAgovCgtmbG9hdF9zaXplcwoEYXhpcxIPYnJvYWRjYXN0X3NpemVzIglVbnNxdWVlemUKNQoPYnJvYWRjYXN0X3NpemVzCg9ub3JtYWxpemVkX3h5eHkSDHNjYWxlZF9ib3hlcyIDTXVsCiQKDHNjYWxlZF9ib3hlcwoFc2hhcGUSBWJveGVzIgZFeHBhbmQKQwoMb3V0cHV0X3NoYXBlEgZsYWJlbHMiD0NvbnN0YW50T2ZTaGFwZSoaCgV2YWx1ZSoOCAEQBzoBCUIFbGFiZWygAQQKRgoMb3V0cHV0X3NoYXBlEgZzY29yZXMiD0NvbnN0YW50T2ZTaGFwZSodCgV2YWx1ZSoRCAEQASIEZmZmP0IFc2NvcmWgAQQSJ3N5bnRoZXRpYy13aC1jb250cmFjdC1ub3QtbW9kZWwtd2VpZ2h0cyoTCAQQBzoEAAEAAUIHaW5kaWNlcyoNCAEQBzoBAUIEYXhpcyoRCAMQBzoEAawCBEIFc2hhcGUqFwgCEAc6AwGsAkIMb3V0cHV0X3NoYXBlKisIAQgBCAQQASIQzczMPc3MTD6amRk/MzMzP0IPbm9ybWFsaXplZF94eXh5WiIKBmltYWdlcxIYChYIAhISCgIIAQoCCAMKAwiABQoDCIAFWiMKEW9yaWdfdGFyZ2V0X3NpemVzEg4KDAgHEggKAggBCgIIAmIZCgZsYWJlbHMSDwoNCAcSCQoCCAEKAwisAmIcCgVib3hlcxITChEIARINCgIIAQoDCKwCCgIIBGIZCgZzY29yZXMSDwoNCAESCQoCCAEKAwisAkIECgAQEg==");
}
