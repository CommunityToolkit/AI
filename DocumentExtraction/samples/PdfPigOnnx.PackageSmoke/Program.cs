// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

if (args.Length != 2)
{
    throw new ArgumentException("Expected selected asset framework and output receipt path.");
}

// Configure, but do not build, optional capabilities requiring curated models.
_ = new PdfPigOnnxDocumentExtractionClientBuilder(new() { LayoutModelPath = "unused-layout.onnx", UseCorrectedLayoutCoordinates = true })
    .WithOcr(ocr =>
    {
        ocr.DetectorModelPath = "unused-detector.onnx";
        ocr.RecognizerModelPath = "unused-recognizer.onnx";
        ocr.Mode = OnnxOcrMode.PagesWithoutNativeText;
    })
    .WithTableStructure(table => table.ModelPath = "unused-table.onnx")
    .WithColumnReadingOrder();

await SyntheticSmoke.RunAsync(args[0]);
var assembly = typeof(PdfPigOnnxDocumentExtractionClient).Assembly;
var marker = assembly.GetCustomAttributesData().Single(attribute =>
    attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute");
string? diagnostic = marker.ConstructorArguments.Single().Value as string;
string? url = marker.NamedArguments.Single(argument => argument.MemberName == "UrlFormat").TypedValue.Value as string;
if (diagnostic != "CTDE0001" ||
    url != "https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics")
{
    throw new InvalidDataException("Missing Toolkit-owned experimental marker or incorrect diagnostic URL.");
}

string receiptPath = Path.GetFullPath(args[1]);
string packageAsset = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "CompanionPackageAsset").Value!;
string packageHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(packageAsset)));
string loadedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location)));
if (loadedHash != packageHash)
{
    throw new InvalidDataException("Loaded companion assembly does not match the explicitly selected actual package asset.");
}

Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(new
{
    Schema = "pdfpig-onnx-synthetic-v1",
    Runtime = RuntimeInformation.FrameworkDescription,
    ExpectedAssetFramework = args[0],
    LoadedFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
    LoadedAssembly = assembly.Location,
    LoadedAssemblySha256 = loadedHash,
    PackageAsset = packageAsset,
    PackageAssetSha256 = packageHash,
    CoreAssemblies = new[]
    {
        typeof(DocumentExtractionClient).Assembly,
        typeof(ExtractionDocument).Assembly,
        typeof(TextContent).Assembly,
        Assembly.Load("Microsoft.Extensions.DocumentExtraction"),
    }.Select(core => new
    {
        Name = core.GetName().Name,
        Path = core.Location,
        InformationalVersion = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Target = core.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core.Location))),
    }).ToArray(),
    CoreProbeScope = "Three abstraction assemblies used by extraction; fourth implementation assembly loaded only for owning-package metadata/hash validation, not middleware execution.",
    ExperimentalDiagnostic = diagnostic,
    ExperimentalUrl = url,
    SyntheticExtraction = "passed: 1 rendered page, 300 contract regions, native literal retained",
    OptionalCapabilities = "configuration only; no curated weights loaded",
}, new JsonSerializerOptions { WriteIndented = true }));
