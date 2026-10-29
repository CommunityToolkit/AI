// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Milvus.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests;

public sealed class MilvusFullTextTests : IClassFixture<MilvusFixture>
{
    [Fact]
    public async Task ChatHistoryMemoryProviderDefinitionStoresReadsAndSearchesMessages()
    {
        // The definition ChatHistoryMemoryProvider of Microsoft.Agents.AI 1.23.0 creates, as a dynamic collection.
        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("Key", typeof(Guid)),
                new VectorStoreDataProperty("Role", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("MessageId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("AuthorName", typeof(string)),
                new VectorStoreDataProperty("ApplicationId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("AgentId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("UserId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("SessionId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("Content", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty("CreatedAt", typeof(string)) { IsIndexed = true },
                new VectorStoreVectorProperty("ContentEmbedding", typeof(string), 4),
            ],
        };
        using var store = MilvusTestStore.Instance.GetVectorStore(new() { EmbeddingGenerator = new ConstantEmbeddingGenerator() });
        using var collection = store.GetDynamicCollection("ct_chat_history_" + Guid.NewGuid().ToString("N"), definition);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.EnsureCollectionExistsAsync();
            var question = Message("user", "What is the capital of France?");
            var answer = Message("assistant", "The capital of France is Paris.");
            var empty = Message("assistant", null);
            await collection.UpsertAsync([question, answer, empty]);

            var read = await collection.GetAsync(answer["Key"]!);
            Assert.NotNull(read);
            Assert.Equal("The capital of France is Paris.", read["Content"]);
            Assert.DoesNotContain("Content_bm25", read.Keys);
            Assert.Null((await collection.GetAsync(empty["Key"]!))!["Content"]);

            var results = await collection.SearchAsync("capital", 3, new() { Filter = r => (string?)r["Role"] == "user" }).ToListAsync();
            Assert.Equal(question["Key"], Assert.Single(results).Record["Key"]);
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }

        static Dictionary<string, object?> Message(string role, string? content) => new()
        {
            ["Key"] = Guid.NewGuid(),
            ["Role"] = role,
            ["MessageId"] = Guid.NewGuid().ToString(),
            ["AuthorName"] = null,
            ["ApplicationId"] = "app",
            ["AgentId"] = "agent",
            ["UserId"] = "user",
            ["SessionId"] = "session",
            ["Content"] = content,
            ["CreatedAt"] = "2026-10-23T10:00:00Z",
            ["ContentEmbedding"] = content ?? string.Empty,
        };
    }

    [Fact]
    public async Task TextSearchStoreSearchesItsDocumentsWithHybridSearch()
    {
        // TextSearchStore<string> of Microsoft.SemanticKernel.Core 1.80.1 creates a typed collection with this definition,
        // and searches it with the query, the words of the query as keywords, and a filter on its namespaces.
        using var store = MilvusTestStore.Instance.GetVectorStore(new() { EmbeddingGenerator = new ConstantEmbeddingGenerator() });
        using var collection = store.GetCollection<string, TextSearchStoreDocument>("ct_text_search_" + Guid.NewGuid().ToString("N"), TextSearchStoreDocument.Definition(4));
        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(
            [
                Document("apples", "Apples are a healthy and nourishing snack", "fruit"),
                Document("oranges", "Oranges are tangy and contain vitamin c", "fruit"),
                Document("grapes", "Grapes are healthy, sweet and juicy", "fruit"),
                Document("grapevine", "A grapevine climbs the wall", "garden"),
                Document("unread", null, "fruit"),
            ]);
            var hybrid = Assert.IsAssignableFrom<IKeywordHybridSearchable<TextSearchStoreDocument>>(
                collection.GetService(typeof(IKeywordHybridSearchable<TextSearchStoreDocument>)));
            var query = "Which fruit is tangy?";
            var keywords = query.Split([' ', '?'], StringSplitOptions.RemoveEmptyEntries);
            var nameSpace = "fruit";

            var results = await hybrid.HybridSearchAsync(query, keywords, 3, new() { Filter = d => d.Namespaces.Contains(nameSpace) }).ToListAsync();

            Assert.Equal(3, results.Count);
            Assert.Equal("oranges", results[0].Record.Key);
            Assert.Equal("Oranges are tangy and contain vitamin c", results[0].Record.Text);
            Assert.DoesNotContain(results, r => r.Record.Key == "grapevine");
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }

        static TextSearchStoreDocument Document(string key, string? text, string nameSpace) => new()
        {
            Key = key,
            Namespaces = [nameSpace],
            Text = text,
            SourceId = key,
            SourceName = key + ".txt",
            SourceLink = "https://example.com/" + key,
            TextEmbedding = text ?? string.Empty,
        };
    }

    [Fact]
    public async Task ChatHistoryMemoryProviderDefinitionSupportsHybridSearch()
    {
        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("Key", typeof(Guid)),
                new VectorStoreDataProperty("Role", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("Content", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreVectorProperty("ContentEmbedding", typeof(string), 4),
            ],
        };
        using var store = MilvusTestStore.Instance.GetVectorStore(new() { EmbeddingGenerator = new ConstantEmbeddingGenerator() });
        using var collection = store.GetDynamicCollection("ct_chat_hybrid_" + Guid.NewGuid().ToString("N"), definition);
        try
        {
            await collection.EnsureCollectionExistsAsync();
            var paris = Guid.NewGuid();
            await collection.UpsertAsync(
            [
                new() { ["Key"] = Guid.NewGuid(), ["Role"] = "user", ["Content"] = "What is the capital of France?", ["ContentEmbedding"] = "q" },
                new() { ["Key"] = paris, ["Role"] = "assistant", ["Content"] = "Paris is the capital of France.", ["ContentEmbedding"] = "a" },
                new() { ["Key"] = Guid.NewGuid(), ["Role"] = "assistant", ["Content"] = "Rome is the capital of Italy.", ["ContentEmbedding"] = "b" },
            ]);
            var hybrid = Assert.IsAssignableFrom<IKeywordHybridSearchable<Dictionary<string, object?>>>(
                collection.GetService(typeof(IKeywordHybridSearchable<Dictionary<string, object?>>)));

            var results = await hybrid.HybridSearchAsync("capital", ["Paris"], 2, new() { Filter = r => (string?)r["Role"] == "assistant" }).ToListAsync();

            Assert.Equal(2, results.Count);
            Assert.Equal(paris, results[0].Record["Key"]);
            Assert.DoesNotContain("Content_bm25", results[0].Record.Keys);
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    private sealed class TextSearchStoreDocument
    {
        public string Key { get; set; } = string.Empty;

        public List<string> Namespaces { get; set; } = [];

        public string? Text { get; set; }

        public string? SourceId { get; set; }

        public string? SourceName { get; set; }

        public string? SourceLink { get; set; }

        public string? TextEmbedding { get; set; }

        public static VectorStoreCollectionDefinition Definition(int dimensions) => new()
        {
            Properties =
            [
                new VectorStoreKeyProperty("Key", typeof(string)),
                new VectorStoreDataProperty("Namespaces", typeof(List<string>)) { IsIndexed = true },
                new VectorStoreDataProperty("SourceId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty("SourceName", typeof(string)),
                new VectorStoreDataProperty("SourceLink", typeof(string)),
                new VectorStoreVectorProperty("TextEmbedding", typeof(string), dimensions),
            ],
        };
    }

    private sealed class ConstantEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new float[] { 1, 0, 0, 0 }))));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
