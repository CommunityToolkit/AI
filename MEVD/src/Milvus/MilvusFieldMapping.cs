// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using Milvus.Client;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Milvus;

internal static class MilvusFieldMapping
{
    public static MilvusDataType GetDataType(Type type)
        => type == typeof(string) ? MilvusDataType.VarChar
            : type == typeof(bool) ? MilvusDataType.Bool
            : type == typeof(int) ? MilvusDataType.Int32
            : type == typeof(long) ? MilvusDataType.Int64
            : type == typeof(float) ? MilvusDataType.Float
            : type == typeof(double) ? MilvusDataType.Double
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

    internal static IReadOnlyList<FieldData> CreateColumns(CollectionModel model, IReadOnlyList<IDictionary<string, object?>> rows)
    {
        List<FieldData> fields = [];
        var key = model.KeyProperty;
        fields.Add(key.Type == typeof(long) ? Scalar<long>(key.StorageName) : Scalar<string>(key.StorageName));
        foreach (var property in model.DataProperties)
        {
            var name = property.StorageName;
            if (GetElementType(property.Type) is { } element)
            {
                fields.Add(element == typeof(string) ? ArrayColumn<string>(name)
                    : element == typeof(bool) ? ArrayColumn<bool>(name)
                    : element == typeof(int) ? ArrayColumn<int>(name)
                    : element == typeof(long) ? ArrayColumn<long>(name)
                    : element == typeof(float) ? ArrayColumn<float>(name)
                    : element == typeof(double) ? ArrayColumn<double>(name)
                    : throw new NotSupportedException($"Unsupported array element type '{element}'."));
            }
            else
            {
                var type = property.Type;
                fields.Add(type == typeof(string) ? Scalar<string?>(name)
                    : type == typeof(bool) ? Scalar<bool>(name) : type == typeof(bool?) ? Scalar<bool?>(name)
                    : type == typeof(int) ? Scalar<int>(name) : type == typeof(int?) ? Scalar<int?>(name)
                    : type == typeof(long) ? Scalar<long>(name) : type == typeof(long?) ? Scalar<long?>(name)
                    : type == typeof(float) ? Scalar<float>(name) : type == typeof(float?) ? Scalar<float?>(name)
                    : type == typeof(double) ? Scalar<double>(name) : type == typeof(double?) ? Scalar<double?>(name)
                    : throw new NotSupportedException($"Unsupported Milvus data type '{type}'."));
            }
        }
        foreach (var property in model.VectorProperties)
        {
            fields.Add(FieldData.CreateFloatVector(property.StorageName, rows.Select(r => (ReadOnlyMemory<float>)r[property.StorageName]!).ToArray()));
        }
        return fields;

        FieldData Scalar<T>(string name) => FieldData.Create(name, rows.Select(r => (T)r[name]!).ToArray());
        FieldData ArrayColumn<T>(string name) => FieldData.CreateArray<T>(name,
            rows.Select(r => r[name] is { } value ? (IReadOnlyList<T>)((IEnumerable<T>)value).ToArray() : null).ToArray());
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
