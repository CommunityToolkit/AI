// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Globalization;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusFilterTranslatorTests
{
    private static string Translate(Expression<Func<MilvusTestRecord, bool>> expression)
        => new MilvusFilterTranslator().Translate(expression,
            new MilvusModelBuilder().Build(typeof(MilvusTestRecord), typeof(Guid), null, null));

    [Fact]
    public void UsesStorageNamesAndEscapesStrings()
    {
        var text = "\" or true or label == \"";
        Assert.Equal("(label is not null and (label == " + MilvusFieldMapping.Literal(text) + "))", Translate(r => r.Name == text));
    }

    [Fact]
    public void TranslatesLogicalAndNullableComparisons()
        => Assert.Equal("((Count is not null and (Count >= 5)) and (not (Enabled == true)))", Translate(r => r.Count >= 5 && !r.Enabled));

    [Fact]
    public void TranslatesNullChecks()
    {
        Assert.Equal("(Count is null)", Translate(r => r.Count == null));
        Assert.Equal("(label is not null)", Translate(r => r.Name != null));
    }

    [Fact]
    public void TranslatesKeyAndCapturedContains()
    {
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var filter = Translate(r => keys.Contains(r.Key));
        Assert.Equal("(record_id in [" + string.Join(", ", keys.Select(k => MilvusFieldMapping.Literal(k))) + "])", filter);
    }

    [Fact]
    public void EmptyContainsMatchesNothing()
    {
        var keys = Array.Empty<Guid>();
        Assert.Equal("(record_id is null)", Translate(r => keys.Contains(r.Key)));
    }

#pragma warning disable CS0464 // Deliberately verify lifted comparisons with literal null.
    [Fact]
    public void RelationalComparisonsWithNullMatchNothing()
    {
        int? value = null;
        Assert.Equal("(record_id is null)", Translate(r => r.Count > default(int?)));
        Assert.Equal("(record_id is null)", Translate(r => r.Count >= value));
        Assert.Equal("(record_id is null)", Translate(r => value < r.Count));
        Assert.Equal("(not (record_id is null))", Translate(r => !(r.Count <= value)));
    }

#pragma warning restore CS0464

    [Fact]
    public void ConstantPredicatesReferenceTheNonNullablePrimaryKey()
    {
        Assert.Equal("(record_id is not null)", Translate(r => true));
        Assert.Equal("(record_id is null)", Translate(r => false));
        Assert.Equal("(Enabled == true)", Translate(r => r.Enabled));
        Assert.Equal("(Enabled == true)", Translate(r => r.Enabled == true));
    }

    [Fact]
    public void InlineContainsUsesScalarLiterals()
        => Assert.Equal("(Count is not null and (Count in [1, 2]))", Translate(r => new int?[] { 1, 2 }.Contains(r.Count)));

    [Fact]
    public void NullableInequalityAndNegationIncludeNullRows()
    {
        Assert.Equal("(Count is null or (Count != 5))", Translate(r => r.Count != 5));
        Assert.Equal("(not (Count is not null and (Count > 5)))", Translate(r => !(r.Count > 5)));
    }

    [Fact]
    public void RejectsUnsupportedMethods()
        => Assert.Throws<NotSupportedException>(() => Translate(r => r.Name!.StartsWith("a")));

    [Fact]
    public void NumericLiteralsAreCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            Assert.Equal("1.25", MilvusFieldMapping.Literal(1.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsNonFiniteNumbers(double value)
        => Assert.Throws<NotSupportedException>(() => MilvusFieldMapping.Literal(value));

    private static string TranslateDynamic(Expression<Func<Dictionary<string, object?>, bool>> expression)
        => new MilvusFilterTranslator().Translate(expression, new MilvusModelBuilder().BuildDynamic(new()
        {
            Properties =
            [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("A", typeof(int?)),
                new VectorStoreDataProperty("B", typeof(int?)),
                new VectorStoreDataProperty("I", typeof(int)),
                new VectorStoreDataProperty("S", typeof(string)),
                new VectorStoreDataProperty("T", typeof(string)),
                new VectorStoreVectorProperty("Vector", typeof(float[]), 3),
            ],
        }, null));

    [Fact]
    public void ComparesTwoNullablePropertiesAsCSharpDoes()
    {
        // == matches two nulls, != matches one null, and a relational operator is false with a null.
        Assert.Equal("((A is null and B is null) or (A is not null and B is not null and (A == B)))", TranslateDynamic(r => (int?)r["A"] == (int?)r["B"]));
        Assert.Equal("((A is null and B is not null) or (A is not null and B is null) or (A is not null and B is not null and (A != B)))",
            TranslateDynamic(r => (int?)r["A"] != (int?)r["B"]));
        Assert.Equal("(A is not null and B is not null and (A < B))", TranslateDynamic(r => (int?)r["A"] < (int?)r["B"]));
        Assert.Equal("(not (A is not null and B is not null and (A >= B)))", TranslateDynamic(r => !((int?)r["A"] >= (int?)r["B"])));
        Assert.Equal("((S is null and T is null) or (S is not null and T is not null and (S == T)))", TranslateDynamic(r => (string?)r["S"] == (string?)r["T"]));
        Assert.Equal("((label is null and label is null) or (label is not null and label is not null and (label == label)))", Translate(r => r.Name == r.Name));
    }

    [Fact]
    public void ComparesANullableAndANonNullablePropertyAsCSharpDoes()
    {
        Assert.Equal("(A is not null and (A == I))", TranslateDynamic(r => (int?)r["A"] == (int)r["I"]!));
        Assert.Equal("(A is null or (I != A))", TranslateDynamic(r => (int)r["I"]! != (int?)r["A"]));
    }
}
