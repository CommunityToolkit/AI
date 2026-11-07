using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommunityToolkit.DocumentExtraction.Tests;

internal static class AzureWireFixture
{
#if CONTENT_UNDERSTANDING
    public static bool IsContentUnderstanding => true;
#else
    public static bool IsContentUnderstanding => false;
#endif
    public static readonly double[] Coordinates = [1, 2, 3, 2, 3, 4, 2, 5, 1, 4];
    public static readonly (double X, double Y)[] Points = [(1, 2), (3, 2), (3, 4), (2, 5), (1, 4)];

    public static JsonObject Create()
    {
        object span = new { offset = 0, length = 80 };
        object[] regions = [new { pageNumber = 3, polygon = Coordinates }];
        object paragraph = IsContentUnderstanding
            ? new { content = "Invoice heading", role = "title", span, source = "D(3,1,2,3,2,3,4,2,5,1,4);D(3);D(3,1,2,3,2,3,4,2,5,1,4)" }
            : new { content = "Invoice heading", role = "title", spans = new[] { span }, boundingRegions = regions };
        object cell = IsContentUnderstanding
            ? new { rowIndex = 0, columnIndex = 0, rowSpan = 2, columnSpan = 2, kind = "columnHeader",
                content = "Total amount", source = "D(3,1,2,3,2,3,4,2,5,1,4)", span }
            : new { rowIndex = 0, columnIndex = 0, rowSpan = 2, columnSpan = 2, kind = "columnHeader",
                content = "Total amount", boundingRegions = regions, spans = new[] { span } };
        object table = IsContentUnderstanding
            ? new { rowCount = 2, columnCount = 3, cells = new[] { cell }, source = "D(3,1,2,3,2,3,4,2,5,1,4)", span }
            : new { rowCount = 2, columnCount = 3, cells = new[] { cell }, boundingRegions = regions, spans = new[] { span } };
        object figure = IsContentUnderstanding
            ? new { id = "3.1", description = "Revenue chart", source = "D(3,1,2,3,2,3,4,2,5,1,4)", span }
            : new { id = "3.1", caption = new { content = "Revenue chart", spans = new[] { span } },
                boundingRegions = regions, spans = new[] { span } };
        object page = IsContentUnderstanding
            ? new { pageNumber = 3, width = 300, height = 400, angle = 0, spans = new[] { span },
                lines = Array.Empty<object>(), words = Array.Empty<object>() }
            : new { pageNumber = 3, width = 300, height = 400, angle = 0, unit = "pixel", spans = new[] { span },
                lines = Array.Empty<object>(), words = Array.Empty<object>(), selectionMarks = Array.Empty<object>() };
        var content = new JsonObject
        {
            ["pages"] = JsonSerializer.SerializeToNode(new[] { page }),
            ["paragraphs"] = JsonSerializer.SerializeToNode(new[] { paragraph }),
            ["tables"] = JsonSerializer.SerializeToNode(new[] { table }),
            ["figures"] = JsonSerializer.SerializeToNode(new[] { figure }),
        };
        if (IsContentUnderstanding)
        {
            content["kind"] = "document";
            content["mimeType"] = "application/pdf";
            content["markdown"] = "Invoice heading";
            content["unit"] = "pixel";
            return new JsonObject
            {
                ["status"] = "succeeded",
                ["id"] = "receipt",
                ["result"] = new JsonObject
                {
                    ["analyzerId"] = "native-analyzer",
                    ["apiVersion"] = "2025-11-01",
                    ["createdAt"] = "2026-01-01T00:00:00Z",
                    ["contents"] = new JsonArray(content),
                },
            };
        }

        content["apiVersion"] = "2024-11-30";
        content["modelId"] = "native-model";
        content["stringIndexType"] = "utf16CodeUnit";
        content["content"] = "Invoice heading";
        content["contentFormat"] = "markdown";
        return new JsonObject
        {
            ["status"] = "succeeded",
            ["createdDateTime"] = "2026-01-01T00:00:00Z",
            ["lastUpdatedDateTime"] = "2026-01-01T00:00:01Z",
            ["analyzeResult"] = content,
        };
    }

    public static JsonObject Content(JsonObject root) => (IsContentUnderstanding
        ? root["result"]!["contents"]![0]! : root["analyzeResult"]!).AsObject();

    public static byte[] Bytes(JsonObject root)
        => Encoding.UTF8.GetBytes(" \n" + root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n \t");

    public static JsonObject Section(params string[] pointers)
    {
        var section = new JsonObject { ["elements"] = JsonSerializer.SerializeToNode(pointers) };
        section[IsContentUnderstanding ? "span" : "spans"] = IsContentUnderstanding
            ? JsonSerializer.SerializeToNode(new { offset = 0, length = 80 })
            : JsonSerializer.SerializeToNode(new[] { new { offset = 0, length = 80 } });
        return section;
    }
}
