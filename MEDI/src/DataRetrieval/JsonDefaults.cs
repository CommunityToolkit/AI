using System.Text.Json;

namespace CommunityToolkit.DataRetrieval;

/// <summary>Shared JSON serialization defaults for LLM response parsing.</summary>
internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
