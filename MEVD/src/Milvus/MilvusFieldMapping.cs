// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using Milvus.Client.V2.Types;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Milvus;

internal static class MilvusFieldMapping
{
    public static DataType GetDataType(Type type)
        => type == typeof(string) ? DataType.VarChar
            : type == typeof(bool) ? DataType.Bool
            : type == typeof(int) ? DataType.Int32
            : type == typeof(long) ? DataType.Int64
            : type == typeof(float) ? DataType.Float
            : type == typeof(double) ? DataType.Double
            : throw new NotSupportedException($"Unsupported Milvus field type '{type}'.");

    internal static Type? GetElementType(Type type)
        => type.IsArray ? type.GetElementType()
            : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GenericTypeArguments[0] : null;

    internal static object? ReadValue(object? value, Type type)
    {
        if (value is null || GetElementType(type) is not { } element)
        {
            return value;
        }
        return element == typeof(string) ? ReadArray<string>(value, type.IsArray)
            : element == typeof(bool) ? ReadArray<bool>(value, type.IsArray)
            : element == typeof(int) ? ReadArray<int>(value, type.IsArray)
            : element == typeof(long) ? ReadArray<long>(value, type.IsArray)
            : element == typeof(float) ? ReadArray<float>(value, type.IsArray)
            : element == typeof(double) ? ReadArray<double>(value, type.IsArray)
            : throw new NotSupportedException($"Unsupported array element type '{element}'.");

        static object ReadArray<T>(object value, bool array)
            => array ? ((IEnumerable<T>)value).ToArray() : ((IEnumerable<T>)value).ToList();
    }

    /// <summary>
    /// Turns the columns of a query or search response into rows keyed by field name.
    /// </summary>
    internal static List<Dictionary<string, object?>> ReadRows(IReadOnlyList<FieldData> columns)
    {
        var count = columns.Count == 0 ? 0 : columns.Max(c => c.RowCount);
        var rows = new List<Dictionary<string, object?>>(count);
        for (var i = 0; i < count; i++)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                if (column.FieldName.Length > 0 && i < column.RowCount)
                {
                    row[column.FieldName] = ReadCell(column, i);
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    private static object? ReadCell(FieldData column, int index)
    {
        if (column.ValidData is { } valid && !valid[index])
        {
            return null;
        }
        return column switch
        {
            FieldData<string> f => f.Data[index],
            FieldData<bool> f => f.Data[index],
            FieldData<bool?> f => f.Data[index],
            FieldData<int> f => f.Data[index],
            FieldData<int?> f => f.Data[index],
            FieldData<long> f => f.Data[index],
            FieldData<long?> f => f.Data[index],
            FieldData<float> f => f.Data[index],
            FieldData<float?> f => f.Data[index],
            FieldData<double> f => f.Data[index],
            FieldData<double?> f => f.Data[index],
            FieldData<ReadOnlyMemory<float>> f => f.Data[index],
            FieldData<IReadOnlyList<string>?> f => f.Data[index],
            FieldData<IReadOnlyList<bool>?> f => f.Data[index],
            FieldData<IReadOnlyList<int>?> f => f.Data[index],
            FieldData<IReadOnlyList<long>?> f => f.Data[index],
            FieldData<IReadOnlyList<float>?> f => f.Data[index],
            FieldData<IReadOnlyList<double>?> f => f.Data[index],
            _ => throw new NotSupportedException($"Unsupported Milvus field data '{column.GetType()}' for field '{column.FieldName}'."),
        };
    }

    public static object ToKey(object key) => key is Guid guid ? guid.ToString("D") : key;

    // Milvus parses backslash escapes, but does not combine JSON surrogate-pair escapes.
    // Keep Unicode code units intact so non-BMP characters reach the server as UTF-8.
    internal static string EscapeString(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }
        return builder.Append('"').ToString();
    }

    public static string Literal(object? value) => value switch
    {
        null => "null",
        string text => EscapeString(text),
        Guid guid => EscapeString(guid.ToString("D")),
        bool boolean => boolean ? "true" : "false",
        int or long => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        float single when !float.IsNaN(single) && !float.IsInfinity(single) => single.ToString("R", CultureInfo.InvariantCulture),
        double number when !double.IsNaN(number) && !double.IsInfinity(number) => number.ToString("R", CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Unsupported Milvus filter value '{value.GetType()}'."),
    };
}
