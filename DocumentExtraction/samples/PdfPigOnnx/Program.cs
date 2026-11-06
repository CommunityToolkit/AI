// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

if (args.Length < 2)
{
    throw new ArgumentException("Usage: PdfPigOnnx <models-directory> <document.pdf> [--ocr] [--tables] [--columns]");
}

string models = Path.GetFullPath(args[0]);
var options = new OnnxLayoutOptions
{
    LayoutModelPath = Path.Combine(models, "heron.onnx"),
    UseCorrectedLayoutCoordinates = true,
};
var builder = new PdfPigOnnxDocumentExtractionClientBuilder(options);
foreach (string capability in args.Skip(2))
{
    switch (capability)
    {
        case "--ocr":
            builder.WithOcr(ocr =>
            {
                ocr.DetectorModelPath = Path.Combine(models, "ch_PP-OCRv5_det_mobile.onnx");
                ocr.RecognizerModelPath = Path.Combine(models, "ch_PP-OCRv5_rec_mobile.onnx");
                ocr.Mode = OnnxOcrMode.PagesWithoutNativeText;
            });
            break;
        case "--tables":
            builder.WithTableStructure(table => table.ModelPath = Path.Combine(models, "tatr-v1.0-structure-fp32.onnx"));
            break;
        case "--columns":
            builder.WithColumnReadingOrder();
            break;
        default:
            throw new ArgumentException($"Unknown capability '{capability}'.");
    }
}

using var client = builder.Build();
using var input = File.OpenRead(args[1]);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var document = await client.ExtractAsync(input, Path.GetFileName(args[1]), "application/pdf", cancellationToken: timeout.Token);
foreach (var element in document.EnumerateContent())
{
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
    if (element is ExtractionDocumentTableElement table)
    {
        foreach (var cell in table.Cells)
        {
            if (cell?.Content?.Content is TextContent cellText)
            {
                Console.WriteLine($"[{cell.RowIndex},{cell.ColumnIndex}] {cellText.Text}");
            }
        }
    }
}
