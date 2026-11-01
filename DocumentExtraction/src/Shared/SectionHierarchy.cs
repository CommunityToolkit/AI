// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction;

internal static class SectionHierarchy
{
    public static void Order(ExtractionDocument document, IReadOnlyDictionary<ExtractionDocumentElement, int> offsets)
    {
        foreach (ExtractionDocumentSection section in document.Sections)
        {
            ExtractionDocumentElement[] ordered = section.Elements
                .OrderBy(element => offsets.TryGetValue(element, out int offset) ? offset : int.MaxValue)
                .ToArray();
            section.Elements.Clear();
            section.Elements.AddRange(ordered);
        }
    }

    public static void Apply(
        ExtractionDocument document,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements,
        IReadOnlyList<(ExtractionDocumentSection Section, IReadOnlyList<string> Elements)> sections)
    {
        if (sections.Count == 0)
        {
            return;
        }

        var paths = new Dictionary<string, ExtractionDocumentElement>(elements, StringComparer.Ordinal);
        for (int index = 0; index < sections.Count; index++)
        {
            paths.Add($"/sections/{index}", sections[index].Section);
        }

        var parents = new Dictionary<ExtractionDocumentElement, ExtractionDocumentSection>();
        foreach (var (section, references) in sections)
        {
            foreach (string reference in references)
            {
                if (!paths.TryGetValue(reference, out ExtractionDocumentElement? element))
                {
                    throw new InvalidDataException($"The provider section references an unsupported element '{reference}'.");
                }

                if (!parents.TryAdd(element, section))
                {
                    throw new InvalidDataException($"The provider element '{reference}' belongs to more than one section.");
                }
            }
        }

        foreach (var (section, _) in sections)
        {
            var visited = new HashSet<ExtractionDocumentSection>();
            ExtractionDocumentSection current = section;
            while (parents.TryGetValue(current, out ExtractionDocumentSection? parent))
            {
                if (!visited.Add(current))
                {
                    throw new InvalidDataException("The provider returned a cyclic section hierarchy.");
                }

                current = parent;
            }
        }

        foreach (ExtractionDocumentSection root in document.Sections)
        {
            root.Elements.RemoveAll(parents.ContainsKey);
        }

        foreach (var (section, references) in sections)
        {
            foreach (string reference in references)
            {
                section.Elements.Add(paths[reference]);
            }

            if (!parents.ContainsKey(section))
            {
                ProviderAdapterHelpers.GetSection(document, section.Sources).Elements.Add(section);
            }
        }
    }
}
