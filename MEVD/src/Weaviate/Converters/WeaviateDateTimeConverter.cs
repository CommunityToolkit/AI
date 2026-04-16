// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.Weaviate;

/// <summary>
/// Converts <see cref="DateTime"/> to RFC 3339 formatted string for Weaviate.
/// </summary>
internal sealed class WeaviateDateTimeConverter : JsonConverter<DateTime>
{
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffK";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dateString = reader.GetString();

        if (string.IsNullOrWhiteSpace(dateString))
        {
            return default;
        }

        // Parse as DateTimeOffset to properly handle timezone, then convert to UTC DateTime.
        // Weaviate may return the timestamp in a different timezone than it was stored in.
        return DateTimeOffset.Parse(dateString, CultureInfo.InvariantCulture).UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // When DateTime.Kind is Unspecified, the 'K' format specifier produces an empty string (no timezone),
        // which violates RFC 3339. Treat Unspecified as UTC so 'K' produces 'Z'.
        if (value.Kind == DateTimeKind.Unspecified)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        writer.WriteStringValue(value.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
    }
}
