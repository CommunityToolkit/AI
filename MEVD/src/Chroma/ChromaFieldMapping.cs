// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.VectorData.ProviderServices;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// Contains helper methods for mapping the properties of a record to a Chroma record.
/// </summary>
internal static class ChromaFieldMapping
{
    /// <summary>
    /// Get the property stored as the Chroma document: the only full-text indexed string property, or <see langword="null"/> when
    /// there is none or more than one.
    /// </summary>
    public static DataPropertyModel? GetDocumentProperty(CollectionModel model)
    {
        var fullTextProperties = model.DataProperties.Where(IsFullTextString).Take(2).ToList();
        return fullTextProperties.Count == 1 ? fullTextProperties[0] : null;
    }

    /// <summary>
    /// Whether the property is a string property with full-text indexing.
    /// </summary>
    public static bool IsFullTextString(DataPropertyModel property)
        => property.IsFullTextIndexed && property.Type == typeof(string);

    /// <summary>
    /// Convert the given key to a Chroma record id.
    /// </summary>
    public static string ToId(object key)
        => key switch
        {
            null => throw new ArgumentNullException(nameof(key)),
            string id => id,
            Guid id => id.ToString("D"),
            _ => throw new NotSupportedException($"The provided key type '{key.GetType().Name}' is not supported by Chroma.")
        };
}
