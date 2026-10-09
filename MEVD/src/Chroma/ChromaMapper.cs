// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using ChromaDB.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// A record as Chroma stores it: an id, an embedding and metadata.
/// </summary>
internal readonly record struct ChromaStorageRecord(string Id, ReadOnlyMemory<float> Embedding, IReadOnlyDictionary<string, object>? Metadata, string? Document);

/// <summary>
/// Mapper between a Chroma record and the consumer data model.
/// </summary>
/// <typeparam name="TRecord">The consumer data model to map to or from.</typeparam>
internal sealed class ChromaMapper<TRecord>(CollectionModel model)
    where TRecord : class
{
    private readonly DataPropertyModel? _documentProperty = ChromaFieldMapping.GetDocumentProperty(model);

    /// <summary>Gets the property stored as the Chroma document: the only string property with full-text indexing, or <see langword="null"/>.</summary>
    public DataPropertyModel? DocumentProperty => _documentProperty;

    /// <summary>Gets a value indicating whether the records have a Chroma document, from the full-text property.</summary>
    public bool HasDocument => _documentProperty is not null;

    public ChromaStorageRecord MapFromDataToStorageModel(TRecord dataModel, int recordIndex, GeneratedEmbeddings<Embedding<float>>?[]? generatedEmbeddings)
    {
        var keyProperty = model.KeyProperty;
        var key = keyProperty.GetValueAsObject(dataModel)
            ?? throw new InvalidOperationException($"Missing key property '{keyProperty.ModelName}' on provided record of type '{typeof(TRecord).Name}'.");

        // The property stored as the document is the document.
        var metadata = ChromaMetadataConvert.ToMetadata(model.DataProperties
            .Where(property => property != _documentProperty)
            .Select(property => new KeyValuePair<string, object?>(property.StorageName, property.GetValueAsObject(dataModel))));

        // There is exactly one vector property, as verified by the model builder.
        Debug.Assert(
            generatedEmbeddings is null || generatedEmbeddings.Length == 1 && generatedEmbeddings[0] is not null,
            "There should be exactly one generated embedding, for the single vector property.");
        var embedding = GetVector(
            model.VectorProperty,
            generatedEmbeddings is null
                ? model.VectorProperty.GetValueAsObject(dataModel)
                : generatedEmbeddings[0]![recordIndex]);

        var document = _documentProperty?.GetValueAsObject(dataModel) as string;

        return new ChromaStorageRecord(ChromaFieldMapping.ToId(key), embedding, metadata, document);

        // The model builder accepts these three vector types only, and the model checks the values of a dynamic record.
        static ReadOnlyMemory<float> GetVector(PropertyModel property, object? embedding)
            => embedding switch
            {
                null => throw new InvalidOperationException($"Vector property '{property.ModelName}' on provided record of type '{typeof(TRecord).Name}' may not be null."),
                Embedding<float> e => e.Vector,
                float[] a => a,
                _ => (ReadOnlyMemory<float>)embedding
            };
    }

    public TRecord MapFromStorageToDataModel(string id, ReadOnlyMemory<float>? embedding, IReadOnlyDictionary<string, object>? metadata, string? document, bool includeVectors)
    {
        var outputRecord = model.CreateRecord<TRecord>()!;

        // The model builder accepts string and Guid keys only.
        model.KeyProperty.SetValueAsObject(outputRecord, model.KeyProperty.Type == typeof(Guid) ? Guid.Parse(id) : id);

        if (includeVectors && embedding is { } vector)
        {
            // The model builder accepts ReadOnlyMemory<float>, Embedding<float> and float[] only.
            var property = model.VectorProperty;
            var vectorType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
            property.SetValueAsObject(
                outputRecord,
                vectorType == typeof(Embedding<float>) ? new Embedding<float>(vector)
                    : vectorType == typeof(float[]) ? vector.ToArray()
                    : (object)vector);
        }

        foreach (var dataProperty in model.DataProperties)
        {
            if (dataProperty == _documentProperty)
            {
                dataProperty.SetValueAsObject(outputRecord, document);
            }
            else if (metadata is not null && metadata.TryGetValue(dataProperty.StorageName, out var value))
            {
                object? propertyValue;
                try
                {
                    propertyValue = ChromaMetadataConvert.FromMetadataValue(value, dataProperty.Type);
                }
                // Another Chroma client can write any value under the key of a property: a value of another type, a date that
                // does not parse, or a number too large for the property.
                catch (InvalidCastException exception)
                {
                    throw ReadFailed(dataProperty, id, exception);
                }

                dataProperty.SetValueAsObject(outputRecord, propertyValue);
            }
            else if (!dataProperty.Type.IsValueType || Nullable.GetUnderlyingType(dataProperty.Type) is not null)
            {
                // A null value is not stored, since Chroma metadata has no null values; a missing one is null.
                dataProperty.SetValueAsObject(outputRecord, null);
            }
        }

        return outputRecord;

        static InvalidOperationException ReadFailed(DataPropertyModel property, string id, Exception exception)
            => new($"Failed to read the metadata key '{property.StorageName}' of the record '{id}' into the property '{property.ModelName}' of type '{property.Type.Name}'.", exception);
    }
}
