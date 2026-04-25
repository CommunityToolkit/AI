// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Shared.Diagnostics;

namespace CommunityToolkit.VectorData.Weaviate;

internal sealed class WeaviateMapper<TRecord>
    where TRecord : class
{
    private readonly string _collectionName;
    private readonly bool _hasNamedVectors;
    private readonly CollectionModel _model;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private readonly string _vectorPropertyName;

    public WeaviateMapper(
        string collectionName,
        bool hasNamedVectors,
        CollectionModel model,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _collectionName = collectionName;
        _hasNamedVectors = hasNamedVectors;
        _model = model;
        _jsonSerializerOptions = jsonSerializerOptions;

        _vectorPropertyName = hasNamedVectors ?
            WeaviateConstants.ReservedVectorPropertyName :
            WeaviateConstants.ReservedSingleVectorPropertyName;
    }

    public JsonObject MapFromDataToStorageModel(TRecord dataModel, int recordIndex, IReadOnlyList<Embedding>?[]? generatedEmbeddings)
    {
        var keyNode = _model.KeyProperty.GetValueAsObject(dataModel) switch
        {
            Guid g => JsonValue.Create(g),

            null => throw new InvalidOperationException("Key property must not be nulL"),
            _ => throw new InvalidOperationException("Key property must be a Guid")
        };

        // Populate data properties.
        var dataNode = new JsonObject();
        foreach (var property in _model.DataProperties)
        {
            if (property.GetValueAsObject(dataModel) is object value)
            {
                // TODO: NativeAOT support, #11963
                dataNode[property.StorageName] = JsonSerializer.SerializeToNode(value, property.Type, _jsonSerializerOptions);
            }
        }

        // Populate vector properties.
        JsonNode? vectorNode = null;

        if (_hasNamedVectors)
        {
            vectorNode = new JsonObject();

            for (var i = 0; i < _model.VectorProperties.Count; i++)
            {
                var property = _model.VectorProperties[i];

                var vector = generatedEmbeddings?[i] is IReadOnlyList<Embedding> ge
                    ? ge[recordIndex]
                    : property.GetValueAsObject(dataModel);

                vectorNode[property.StorageName] = vector switch
                {
                    ReadOnlyMemory<float> e => BuildJsonArray(e),
                    Embedding<float> e => BuildJsonArray(e.Vector),
                    float[] a => BuildJsonArray(a),

                    null => null,

                    _ => throw new UnreachableException()
                };
            }
        }
        else
        {
            var vector = generatedEmbeddings?[0] is IReadOnlyList<Embedding> ge
                ? ge[recordIndex]
                : _model.VectorProperty.GetValueAsObject(dataModel);

            vectorNode = vector switch
            {
                ReadOnlyMemory<float> e => BuildJsonArray(e),
                Embedding<float> e => BuildJsonArray(e.Vector),
                float[] a => BuildJsonArray(a),

                null => null,

                _ => throw new UnreachableException()
            };
        }

        return new JsonObject
        {
            { WeaviateConstants.CollectionPropertyName, JsonValue.Create(_collectionName) },
            { WeaviateConstants.ReservedKeyPropertyName, keyNode },
            { WeaviateConstants.ReservedDataPropertyName, dataNode },
            { _vectorPropertyName, vectorNode },
        };

        static JsonArray BuildJsonArray(ReadOnlyMemory<float> memory)
        {
            var jsonArray = new JsonArray();

            foreach (var item in memory.Span)
            {
                jsonArray.Add(JsonValue.Create(item));
            }

            return jsonArray;
        }
    }

    public TRecord MapFromStorageToDataModel(JsonObject storageModel, bool includeVectors)
    {
        Throw.IfNull(storageModel);

        var record = _model.CreateRecord<TRecord>()!;

        if (storageModel[WeaviateConstants.ReservedKeyPropertyName]?.GetValue<Guid>() is not Guid key)
        {
            throw new InvalidOperationException("No key property was found in the record retrieved from storage.");
        }

        _model.KeyProperty.SetValueAsObject(record, key);

        // Populate data properties.
        if (storageModel[WeaviateConstants.ReservedDataPropertyName] is JsonObject dataPropertiesJson)
        {
            foreach (var property in _model.DataProperties)
            {
                if (dataPropertiesJson.TryGetPropertyValue(property.StorageName, out var dataValue))
                {
                    // TODO: NativeAOT support, #11963
                    property.SetValueAsObject(record, dataValue?.Deserialize(property.Type, _jsonSerializerOptions));
                }
            }
        }

        // Populate vector properties.
        if (includeVectors)
        {
            if (_hasNamedVectors && storageModel[_vectorPropertyName] is JsonObject vectorPropertiesJson)
            {
                foreach (var property in _model.VectorProperties)
                {
                    if (vectorPropertiesJson.TryGetPropertyValue(property.StorageName, out var node))
                    {
                        PopulateVectorProperty(record, node, property);
                    }
                }
            }
            else
            {
                if (_model.VectorProperties is [var property]
                    && storageModel.TryGetPropertyValue(_vectorPropertyName, out var node))
                {
                    PopulateVectorProperty(record, node, property);
                }
            }
        }

        return record;

        static void PopulateVectorProperty(TRecord record, object? value, VectorPropertyModel property)
        {
            switch (value)
            {
                case null:
                    property.SetValueAsObject(record, null);
                    return;

                case JsonArray jsonArray:
                    property.SetValueAsObject(record, (Nullable.GetUnderlyingType(property.Type) ?? property.Type) switch
                    {
                        var t when t == typeof(ReadOnlyMemory<float>) => new ReadOnlyMemory<float>(jsonArray.GetValues<float>().ToArray()),
                        var t when t == typeof(float[]) => jsonArray.GetValues<float>().ToArray(),
                        var t when t == typeof(Embedding<float>) => new Embedding<float>(jsonArray.GetValues<float>().ToArray()),

                        _ => throw new UnreachableException()
                    });
                    return;

                default:
                    throw new InvalidOperationException("Non-array JSON node received for vector property");
            }
        }
    }
}
