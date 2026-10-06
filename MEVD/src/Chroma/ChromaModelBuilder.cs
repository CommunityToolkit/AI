// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Chroma;

internal class ChromaModelBuilder() : CollectionModelBuilder(s_modelBuildingOptions)
{
    internal const string SupportedVectorTypes = "ReadOnlyMemory<float>, Embedding<float>, float[]";

    // A Chroma record has exactly one embedding.
    private static readonly CollectionModelBuildingOptions s_modelBuildingOptions = new()
    {
        RequiresAtLeastOneVector = true,
        SupportsMultipleVectors = false,
    };

    protected override void ValidateKeyProperty(KeyPropertyModel keyProperty)
    {
        base.ValidateKeyProperty(keyProperty);

        var type = keyProperty.Type;

        if (type != typeof(string) && type != typeof(Guid))
        {
            throw new NotSupportedException(
                $"Property '{keyProperty.ModelName}' has unsupported type '{type.Name}'. Key properties must be either string or Guid.");
        }
    }

    // Checked when the collection object is constructed, rather than when it is first used.
    protected override void ValidateProperty(PropertyModel propertyModel, VectorStoreCollectionDefinition? definition)
    {
        base.ValidateProperty(propertyModel, definition);

        switch (propertyModel)
        {
            case VectorPropertyModel vectorProperty:
                if (vectorProperty.IndexKind is not null and not IndexKind.Hnsw)
                {
                    throw new NotSupportedException(
                        $"Index kind '{vectorProperty.IndexKind}' for {nameof(VectorStoreVectorProperty)} '{vectorProperty.ModelName}' is not supported by the Chroma VectorStore. " +
                        $"Supported index kinds: {IndexKind.Hnsw}.");
                }

                // Throws for a distance function Chroma does not support.
                _ = ChromaCollectionCreateMapping.GetSpace(vectorProperty);
                break;

            case DataPropertyModel { IsFullTextIndexed: true } dataProperty when dataProperty.Type != typeof(string):
                throw new InvalidOperationException(
                    $"Property '{dataProperty.ModelName}' has {nameof(VectorStoreDataProperty.IsFullTextIndexed)} set, but is not a string: Chroma indexes the text of string properties only.");
        }
    }

    protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = "string, int, long, double, float, bool, DateTime, DateTimeOffset,"
#if NET
            + " DateOnly,"
#endif
            + " or arrays/lists of these types";

        if (Nullable.GetUnderlyingType(type) is Type underlyingType)
        {
            type = underlyingType;
        }

        return IsValid(type)
            || (type.IsArray && IsValid(type.GetElementType()!))
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && IsValid(type.GenericTypeArguments[0]));

        static bool IsValid(Type type)
            => type == typeof(string) ||
                type == typeof(int) ||
                type == typeof(long) ||
                type == typeof(double) ||
                type == typeof(float) ||
                type == typeof(bool) ||
                type == typeof(DateTime) ||
#if NET
                type == typeof(DateOnly) ||
#endif
                type == typeof(DateTimeOffset);
    }

    protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes)
        => IsVectorPropertyTypeValidCore(type, out supportedTypes);

    internal static bool IsVectorPropertyTypeValidCore(Type type, [NotNullWhen(false)] out string? supportedTypes)
    {
        supportedTypes = SupportedVectorTypes;

        return type == typeof(ReadOnlyMemory<float>)
            || type == typeof(ReadOnlyMemory<float>?)
            || type == typeof(Embedding<float>)
            || type == typeof(float[]);
    }
}
