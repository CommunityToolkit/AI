// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusArrayFilterTests
{
    private static string Translate(Expression<Func<Record, bool>> filter)
        => new MilvusFilterTranslator().Translate(filter,
            new MilvusModelBuilder().Build(typeof(Record), typeof(string), null, null));

    [Fact]
    public void ArrayContainsGuardsNullFieldsBeforeNegation()
    {
        Assert.Equal("(tags is not null and array_contains(tags, \"x\"))", Translate(r => r.Tags!.Contains("x")));
        Assert.Equal("(tags is null or not array_contains(tags, \"x\"))", Translate(r => !r.Tags!.Contains("x")));
        Assert.Equal("(ListTags is not null and array_contains(ListTags, \"x\"))", Translate(r => r.ListTags!.Contains("x")));
    }

    [Fact]
    public void ArrayAnyGuardsNullFieldsAndDiscardsNullCandidates()
    {
        var candidates = new[] { "x", null };
        const string expected = "(tags is not null and array_contains_any(tags, [\"x\"]))";
        Assert.Equal(expected, Translate(r => r.Tags!.Any(t => new[] { "x", null }.Contains(t))));
        Assert.Equal(expected, Translate(r => r.Tags!.Any(t => candidates.Contains(t))));
        Assert.Equal("(tags is null or not array_contains_any(tags, [\"x\"]))", Translate(r => !r.Tags!.Any(t => candidates.Contains(t))));
        Assert.Equal("(ListTags is not null and array_contains_any(ListTags, [\"x\"]))", Translate(r => r.ListTags!.Any(t => candidates.Contains(t))));
    }

    [Fact]
    public void EmptyAndAllNullCandidatesMatchNothing()
    {
        var empty = Array.Empty<string>();
        var nulls = new string?[] { null, null };
        Assert.Equal("(Id is null)", Translate(r => r.Tags!.Any(t => empty.Contains(t))));
        Assert.Equal("(Id is null)", Translate(r => r.Tags!.Any(t => nulls.Contains(t))));
        Assert.Equal("(Id is not null)", Translate(r => !r.Tags!.Any(t => nulls.Contains(t))));
        Assert.Equal("(Id is null)", Translate(r => r.Tags!.Contains(null!)));
    }

    [Fact]
    public void CompoundNegationPreservesNullArrayMembership()
    {
        Assert.Equal("((tags is null or not array_contains(tags, \"x\")) or (ListTags is null or not array_contains(ListTags, \"y\")))",
            Translate(r => !(r.Tags!.Contains("x") && r.ListTags!.Contains("y"))));
        Assert.Equal("((tags is null or not array_contains(tags, \"x\")) and (ListTags is null or not array_contains(ListTags, \"y\")))",
            Translate(r => !(r.Tags!.Contains("x") || r.ListTags!.Contains("y"))));
        Assert.Equal("(tags is not null and array_contains(tags, \"x\"))", Translate(r => !!r.Tags!.Contains("x")));
    }

    public sealed class Record
    {
        [VectorStoreKey] public string Id { get; set; } = string.Empty;
        [VectorStoreData(StorageName = "tags")] public string[]? Tags { get; set; }
        [VectorStoreData] public List<string>? ListTags { get; set; }
        [VectorStoreVector(3)] public ReadOnlyMemory<float> Vector { get; set; }
    }
}
