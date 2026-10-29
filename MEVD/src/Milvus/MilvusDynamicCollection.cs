// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.Client;

namespace CommunityToolkit.VectorData.Milvus;

/// <summary>
/// Represents a collection of vector store records in a Milvus database, mapped to a dynamic <c>Dictionary&lt;string, object?&gt;</c>.
/// </summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public sealed class MilvusDynamicCollection : MilvusCollection<object, Dictionary<string, object?>>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MilvusDynamicCollection"/> class.
    /// </summary>
    /// <param name="milvusClient">Milvus client that can be used to manage the collections and points in a Milvus store.</param>
    /// <param name="name">The name of the collection.</param>
    /// <param name="ownsClient">A value indicating whether <paramref name="milvusClient"/> is disposed when the collection is disposed.</param>
    /// <param name="options">Optional configuration options for this class.</param>
    public MilvusDynamicCollection(MilvusClient milvusClient, string name, bool ownsClient, MilvusCollectionOptions options)
        : this(() => new ClientWrapper(milvusClient, ownsClient), name, options)
    {
    }

    internal MilvusDynamicCollection(Func<ClientWrapper> clientFactory, string name, MilvusCollectionOptions options)
        : base(
            clientFactory,
            name,
            static options => new MilvusModelBuilder()
                .BuildDynamic(
                    options.Definition ?? throw new ArgumentException("Definition is required for dynamic collections"),
                    options.EmbeddingGenerator),
            options)
    {
    }
}
