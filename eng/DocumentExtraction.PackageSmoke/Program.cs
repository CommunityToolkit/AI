// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.AI.DocumentIntelligence;
using CommunityToolkit.DocumentExtraction.AzureContentUnderstanding;
using CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;
using CommunityToolkit.DocumentExtraction.Mistral;
using CommunityToolkit.DocumentExtraction.PdfPig;
using CommunityToolkit.DocumentExtraction.PdfPig.UnitTests;
using CommunityToolkit.DocumentExtraction.Vision;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

if (args.Length != 2)
{
    throw new ArgumentException("Supply the expected asset TFM and an output receipt path.");
}

Type[] types = [typeof(PdfPigDocumentExtractionClient), typeof(ContentUnderstandingExtractionClient),
    typeof(DocumentIntelligenceExtractionClient), typeof(MistralDocumentExtractionClient), typeof(VisionDocumentExtractionClient)];
string expectedFramework = args[0] == "netstandard2.0" ? ".NETStandard,Version=v2.0"
    : args[0] == "net8.0" ? ".NETCoreApp,Version=v8.0" : ".NETCoreApp,Version=v10.0";
var assemblies = new List<object>();
foreach (Type type in types)
{
    Assembly assembly = type.Assembly;
    if (!typeof(DocumentExtractionClient).IsAssignableFrom(type) ||
        assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName != expectedFramework)
    {
        throw new InvalidDataException($"Wrong provider contract or asset: {type}.");
    }

    CustomAttributeData marker = assembly.GetCustomAttributesData().Single(attribute =>
        attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute");
    if (!Equals(marker.ConstructorArguments.Single().Value, "CTDE0001") ||
        !Equals(marker.NamedArguments.Single(argument => argument.MemberName == "UrlFormat").TypedValue.Value,
            "https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics"))
    {
        throw new InvalidDataException($"Wrong experimental metadata: {type}.");
    }

    assemblies.Add(new { type = type.FullName, path = assembly.Location,
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
        target = expectedFramework, diagnostic = "CTDE0001" });
}

foreach (string name in new[] { "OcrEndpoint", "DefaultModel" })
{
    PropertyInfo property = typeof(MistralDocumentExtractionClientOptions).GetProperty(name)!;
    if (!property.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute") ||
        !property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(modifier =>
            modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
    {
        throw new InvalidDataException($"Mistral {name} lost its required/init contract.");
    }
}

// Construct the service adapters without sending a service request.
var contentUnderstandingClient = new ContentUnderstandingClient(new Uri("https://offline.invalid"), new AzureKeyCredential("offline"));
using DocumentExtractionClient cu = new ContentUnderstandingExtractionClient(contentUnderstandingClient);
var documentIntelligenceClient = new DocumentIntelligenceClient(new Uri("https://offline.invalid"), new AzureKeyCredential("offline"));
using DocumentExtractionClient di = new DocumentIntelligenceExtractionClient(documentIntelligenceClient);
using DocumentExtractionClient pdf = new PdfPigDocumentExtractionClient();
using var input = new MemoryStream(PdfFixture.Create());
ExtractionDocument document = await pdf.ExtractAsync(input, "invoice", "application/pdf");
if (!input.CanRead || document.SourcePages.Count != 3 ||
    !document.EnumerateContent().Any(element => element.Content is TextContent text && text.Text.Contains("Native invoice 42", StringComparison.Ordinal)))
{
    throw new InvalidDataException("Native PDF extraction or borrowed-input behavior failed.");
}

using var httpClient = new HttpClient(new OfflineHandler());
using DocumentExtractionClient mistral = new MistralDocumentExtractionClient(
    new MistralDocumentExtractionClientOptions
    {
        OcrEndpoint = new Uri("https://api.mistral.ai/v1/ocr"),
        DefaultModel = "mistral-ocr-latest",
    }, "offline-key", httpClient);
input.Position = 0;
ExtractionDocument mistralResult = await mistral.ExtractAsync(input, "invoice", "application/pdf");
if (!mistralResult.EnumerateContent().Any(element => element.Content is TextContent text && text.Text == "offline invoice"))
{
    throw new InvalidDataException("Offline Mistral mapping failed.");
}

using var chatClient = new OfflineChatClient();
using DocumentExtractionClient vision = new VisionDocumentExtractionClient(chatClient);
using DocumentExtractionClient structuredVision = new VisionDocumentExtractionClient(
    chatClient, new VisionDocumentExtractionClientOptions { UseStructuredOutput = true });
input.Position = 0;
ExtractionDocument visionResult = await vision.ExtractAsync(input, "invoice", "application/pdf");
if (!visionResult.EnumerateContent().Any(element => element.Content is TextContent text && text.Text == "offline invoice"))
{
    throw new InvalidDataException("Offline Vision mapping failed.");
}

File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new
{
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    providerAsset = args[0], assemblies, pdfPages = document.SourcePages.Count,
    fiveConstructorsCompiledAndExecuted = true, mistralRequiredInitContract = true,
    liveServiceCalls = 0, modelInferenceCalls = 0,
}, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Actual {args[0]} provider packages: constructors, metadata, native PDF and offline Mistral/Vision passed.");

internal sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"pages":[{"index":0,"markdown":"offline invoice"}]}"""),
        });
    }
}

internal sealed class OfflineChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "offline invoice")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The offline fixture supports only non-streaming extraction.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
