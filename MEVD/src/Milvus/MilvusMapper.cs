// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Milvus;

internal sealed class MilvusMapper<TRecord>(CollectionModel model) where TRecord : class
{
    public IDictionary<string, object?> MapFromDataToStorageModel(TRecord record, int recordIndex,
        GeneratedEmbeddings<Embedding<float>>?[]? generatedEmbeddings)
    {
        var key = model.KeyProperty.GetValueAsObject(record)
            ?? throw new InvalidOperationException("The key cannot be null.");
        Dictionary<string, object?> row = new(StringComparer.Ordinal)
        {
            [model.KeyProperty.StorageName] = MilvusFieldMapping.ToKey(key),
        };
        foreach (var property in model.DataProperties)
        {
            var value = property.GetValueAsObject(record);
            if (value is IEnumerable elements && MilvusFieldMapping.GetElementType(property.Type) is not null)
            {
                var elementIndex = 0;
                foreach (var element in elements)
                {
                    if (element is null)
                    {
                        throw new ArgumentException(
                            $"Record '{key}' (batch index {recordIndex}) has a null element at index {elementIndex} in array property '{property.ModelName}' (storage field '{property.StorageName}'). Milvus array elements cannot be null.",
                            nameof(record));
                    }
                    elementIndex++;
                }
            }
            row[property.StorageName] = value;
        }
        for (var i = 0; i < model.VectorProperties.Count; i++)
        {
            var property = model.VectorProperties[i];
            var vector = GetVector(generatedEmbeddings?[i] is { } embeddings
                ? embeddings[recordIndex] : property.GetValueAsObject(record));
            if (vector.Length != property.Dimensions)
            {
                throw new ArgumentException($"Vector '{property.ModelName}' must have {property.Dimensions} dimensions.");
            }
            row[property.StorageName] = vector;
        }
        return row;
    }

    internal static ReadOnlyMemory<float> GetVector(object? value) => value switch
    {
        ReadOnlyMemory<float> memory => memory,
        float[] array => array,
        Embedding<float> embedding => embedding.Vector,
        _ => throw new InvalidOperationException("A vector must be a non-null float array, ReadOnlyMemory<float> or Embedding<float>."),
    };

    public TRecord MapFromStorageToDataModel(IReadOnlyDictionary<string, object?> row, bool includeVectors)
    {
        var record = model.CreateRecord<TRecord>()!;
        var key = row[model.KeyProperty.StorageName]!;
        model.KeyProperty.SetValueAsObject(record, model.KeyProperty.Type == typeof(Guid) ? Guid.Parse((string)key) : key);
        foreach (var property in model.DataProperties)
        {
            if (row.TryGetValue(property.StorageName, out var value))
            {
                property.SetValueAsObject(record, MilvusFieldMapping.ReadValue(value, property.Type));
            }
        }
        if (includeVectors)
        {
            foreach (var property in model.VectorProperties)
            {
                if (row.TryGetValue(property.StorageName, out var value))
                {
                    var vector = GetVector(value);
                    var type = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
                    property.SetValueAsObject(record, type == typeof(float[]) ? vector.ToArray()
                        : type == typeof(Embedding<float>) ? new Embedding<float>(vector) : (object)vector);
                }
            }
        }
        return record;
    }
}
