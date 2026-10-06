// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ChromaDB.Client;

namespace CommunityToolkit.VectorData.Chroma;

/// <summary>
/// Represents a collection of vector store records in a Chroma database, mapped to a dynamic <c>Dictionary&lt;string, object?&gt;</c>.
/// </summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public sealed class ChromaDynamicCollection : ChromaCollection<object, Dictionary<string, object?>>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaDynamicCollection"/> class.
    /// </summary>
    /// <param name="chromaClient">Chroma client that can be used to manage the collections and records in a Chroma store.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="chromaClient"/> is disposed when the collection is disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    public ChromaDynamicCollection(ChromaClient chromaClient, string name, bool ownsClient, ChromaCollectionOptions options)
        : this(() => new SharedChromaClient(chromaClient, ownsClient), name, options)
    {
    }

    internal ChromaDynamicCollection(Func<SharedChromaClient> clientFactory, string name, ChromaCollectionOptions options)
        : base(
            clientFactory,
            name,
            static options => new ChromaModelBuilder()
                .BuildDynamic(
                    options.Definition ?? throw new ArgumentException("Definition is required for dynamic collections"),
                    options.EmbeddingGenerator),
            options)
    {
    }
}
