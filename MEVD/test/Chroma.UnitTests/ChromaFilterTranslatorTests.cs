// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using ChromaDB.Client;
using CommunityToolkit.VectorData.Chroma;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace Chroma.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChromaFilterTranslator"/> class.
/// </summary>
public class ChromaFilterTranslatorTests
{
    [Fact]
    public void TranslatesContainsOverAnInlineArray()
        => Assert.Equal("""{"HotelName":{"$nin":["a","b"]}}""", Translate(h => !new[] { "a", "b" }.Contains(h.HotelName)));

    [Fact]
    public void TranslatesFalseToNoRecord()
        => Assert.Same(ChromaWhereOperator.None, TranslateFilter(h => false));

    [Fact]
    public void ThrowsForAComparisonOnAString()
        => Assert.Throws<NotSupportedException>(() => Translate(h => string.Compare(h.HotelName, "b", StringComparison.Ordinal) > 0));

    [Fact]
    public void TranslatesANegatedContainsOverAnArrayProperty()
        => Assert.Equal("""{"Tags":{"$not_contains":"pool"}}""", Translate(h => !h.Tags!.Contains("pool")));

    [Fact]
    public void TranslatesANegatedAnyWithContainsToAnAndOfNotContains()
        => Assert.Equal(
            """{"$and":[{"Tags":{"$not_contains":"pool"}},{"Tags":{"$not_contains":"spa"}}]}""",
            Translate(h => !h.Tags!.Any(t => new[] { "pool", "spa" }.Contains(t))));

    [Fact]
    public void TranslatesAnyOverAnEmptyArrayToNoRecord()
        => Assert.Same(ChromaWhereOperator.None, TranslateFilter(h => h.Tags!.Any(t => new string[0].Contains(t))));

    [Fact]
    public void TranslatesANegatedAnyOverAnEmptyArrayToMatchAll()
        => AssertMatchesAll(TranslateFilter(h => !h.Tags!.Any(t => new string[0].Contains(t))));

    [Fact]
    public void TranslatesContainsOverAnEmptyInlineArrayToNoRecord()
        => Assert.Same(ChromaWhereOperator.None, TranslateFilter(h => new string[0].Contains(h.HotelName)));

    [Fact]
    public void TranslatesContainsOverAnEmptyCapturedListToNoRecord()
    {
        var names = new List<string>();

        Assert.Same(ChromaWhereOperator.None, TranslateFilter(h => names.Contains(h.HotelName!)));
    }

    [Fact]
    public void TranslatesANegatedContainsOverAnEmptyInlineArrayToMatchAll()
        => AssertMatchesAll(TranslateFilter(h => !new string[0].Contains(h.HotelName)));

    [Fact]
    public void DropsAnOrBranchThatMatchesNoRecord()
        => Assert.Equal("""{"Rating":{"$gte":4}}""", Translate(h => new string[0].Contains(h.HotelName) || h.Rating >= 4));

    [Fact]
    public void TranslatesAnAndWithABranchThatMatchesNoRecordToNoRecord()
        => Assert.Same(ChromaWhereOperator.None, TranslateFilter(h => new string[0].Contains(h.HotelName) && h.Rating >= 4));

    [Fact]
    public void TranslatesKeyEqualityToTheIds()
        => Assert.Equal("""{"#id":{"$in":["h1"]}}""", Translate(h => h.HotelId == "h1"));

    [Fact]
    public void TranslatesKeyEqualityWithTheConstantOnTheLeftToTheIds()
        => Assert.Equal("""{"#id":{"$in":["h1"]}}""", Translate(h => "h1" == h.HotelId));

    [Fact]
    public void TranslatesContainsOverAListOfKeysToTheIds()
    {
        var keys = new List<string> { "h1", "h2", "h1" };

        Assert.Equal("""{"#id":{"$in":["h1","h2"]}}""", Translate(h => keys.Contains(h.HotelId)));
    }

    [Fact]
    public void TranslatesKeyConditionsWithTheOtherConditions()
        => Assert.Equal(
            """{"$or":[{"$and":[{"Rating":{"$gte":4}},{"#id":{"$in":["h1","h2"]}}]},{"#id":{"$nin":["h3"]}}]}""",
            Translate(h => h.Rating >= 4 && new[] { "h1", "h2" }.Contains(h.HotelId) || h.HotelId != "h3"));

    [Fact]
    public void TranslatesContainsOverAnEmptyListOfKeysToNoRecord()
        => Assert.Same(ChromaWhereOperator.None, TranslateFilter(h => new string[0].Contains(h.HotelId)));

    [Fact]
    public void TranslatesAGuidKeyToItsId()
    {
        var key = new Guid("11111111-2222-3333-4444-555555555555");

        Assert.Equal("""{"#id":{"$in":["11111111-2222-3333-4444-555555555555"]}}""", Translate<ChromaHotel<Guid>>(h => h.HotelId == key).ToString());
    }

    [Fact]
    public void ThrowsForTheKeyComparedWithAProperty()
        => Assert.Throws<NotSupportedException>(() => Translate(h => h.HotelId == h.HotelName));

    [Fact]
    public void TranslatesContainsOnTheFullTextPropertyToADocumentFilter()
        => Assert.Equal("""{"#document":{"$contains":"pool"}}""", TranslateFullText(h => h.Description!.Contains("pool")).ToString());

    [Fact]
    public void TranslatesTextConditionsWithTheOtherConditions()
        => Assert.Equal(
            """{"$or":[{"$and":[{"#document":{"$contains":"pool"}},{"Rating":{"$gte":4}}]},{"#document":{"$not_contains":"spa"}}]}""",
            TranslateFullText(h => h.Description!.Contains("pool") && h.Rating >= 4 || !h.Description.Contains("spa")).ToString());

    [Fact]
    public void ThrowsForContainsOnAStringPropertyThatIsNotTheDocument()
        => Assert.Throws<NotSupportedException>(() => TranslateFilter(h => h.HotelName!.Contains("Grand")));

    [Fact]
    public void EqualityOnADateTimeOffsetMatchesItsUtcForm()
    {
        var opened = new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal("""{"Opened":{"$eq":"2026-10-05T11:00:00.0000000+00:00"}}""", TranslateDated(h => h.Opened == opened));
        Assert.Equal("""{"Opened":{"$ne":"2026-10-05T11:00:00.0000000+00:00"}}""", TranslateDated(h => h.Opened != opened));
    }

    [Fact]
    public void EqualityOnADateTimeMatchesItsOwnKind()
    {
        var updated = new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Unspecified);
        var utc = DateTime.SpecifyKind(updated, DateTimeKind.Utc);

        Assert.Equal("""{"Updated":{"$eq":"2026-10-05T11:00:00.0000000"}}""", TranslateDated(h => h.Updated == updated));
        Assert.Equal("""{"Updated":{"$eq":"2026-10-05T11:00:00.0000000Z"}}""", TranslateDated(h => h.Updated == utc));
    }

    [Fact]
    public void ContainsOnADateTimeOffsetArrayMatchesItsUtcForm()
    {
        var visit = new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal("""{"Visits":{"$contains":"2026-10-05T11:00:00.0000000+00:00"}}""", TranslateDated(h => h.Visits!.Contains(visit)));
        Assert.Equal("""{"Visits":{"$not_contains":"2026-10-05T11:00:00.0000000+00:00"}}""", TranslateDated(h => !h.Visits!.Contains(visit)));
    }

    [Theory]
    [InlineData(">", """{"Price":{"$gt":5.0}}""")]
    [InlineData("<=", """{"Price":{"$lte":5.0}}""")]
    [InlineData("5 <", """{"Price":{"$gt":5.0}}""")]
    [InlineData("5 <=", """{"Price":{"$gte":5.0}}""")]
    [InlineData("5 >=", """{"Price":{"$lte":5.0}}""")]
    [InlineData("!>", """{"Price":{"$lte":5.0}}""")]
    [InlineData("!<", """{"Price":{"$gte":5.0}}""")]
    [InlineData("!<=", """{"Price":{"$gt":5.0}}""")]
    public void TranslatesEachComparison(string comparison, string expected)
    {
        Expression<Func<ChromaHotel<string>, bool>> filter = comparison switch
        {
            ">" => h => h.Price > 5,
            "<=" => h => h.Price <= 5,
            "5 <" => h => 5 < h.Price,
            "5 <=" => h => 5 <= h.Price,
            "5 >=" => h => 5 >= h.Price,
            "!>" => h => !(h.Price > 5),
            "!<" => h => !(h.Price < 5),
            "!<=" => h => !(h.Price <= 5),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };

        Assert.Equal(expected, Translate(filter));
    }

    [Fact]
    public void ConvertsACapturedNumberToTheTypeOfTheProperty()
    {
        // The compiler converts a captured number to the type of the property, as C# converts it implicitly.
        var intValue = 5;
        var longValue = 5L;
        var floatValue = 5.5f;

        Assert.Equal("""{"Price":{"$gt":5.0}}""", Translate(h => h.Price > intValue));
        Assert.Equal("""{"Price":{"$gt":5.0}}""", Translate(h => h.Price > longValue));
        Assert.Equal("""{"Price":{"$gt":5.5}}""", Translate(h => h.Price > floatValue));
        Assert.Equal("""{"Stars":{"$lt":5.0}}""", Translate<NumberHotel>(h => h.Stars < intValue).ToString());
        Assert.Equal("""{"Stars":{"$lt":5.0}}""", Translate<NumberHotel>(h => h.Stars < longValue).ToString());
        Assert.Equal("""{"Visits":{"$lte":5}}""", Translate<NumberHotel>(h => h.Visits <= intValue).ToString());
    }

    [Fact]
    public void ThrowsForAnExplicitCastOfACapturedNumber()
    {
        // An explicit cast can change the value: (long)5.5f is 5.
        var floatValue = 5.5f;
        var doubleValue = 5.5;
        var decimalValue = 5.5m;
        var longValue = 5L;

        Assert.Throws<NotSupportedException>(() => Translate<NumberHotel>(h => h.Visits >= (long)floatValue));
        Assert.Throws<NotSupportedException>(() => Translate<NumberHotel>(h => h.Stars >= (float)doubleValue));
        Assert.Throws<NotSupportedException>(() => Translate(h => h.Price >= (double)decimalValue));
        Assert.Throws<NotSupportedException>(() => TranslateFullText(h => h.Rating == (int)longValue));
    }

    [Fact]
    public void ThrowsForAComparisonOnADate()
        => Assert.Throws<NotSupportedException>(() => TranslateDated(h => h.Opened > DateTimeOffset.UnixEpoch));

    [Fact]
    public void TranslatesABoolPropertyBehindACast()
        => Assert.Equal("""{"parking_is_included":{"$eq":true}}""", Translate(h => (bool)(object)h.Parking));

    [Fact]
    public void TranslatesAComparisonWithNullToNoRecordAndItsNegationToEveryRecord()
    {
        // As in C#, a comparison with null is false whatever the value.
        int? none = null;

        Assert.Same(ChromaWhereOperator.None, TranslateFilter(h => h.Rating > none));
        AssertMatchesAll(TranslateFilter(h => !(h.Rating > none)));
    }

    [Fact]
    public void ThrowsForANegatedComparisonOnANullableProperty()
    {
        Assert.Throws<NotSupportedException>(() => Translate(h => !(h.Rating > 4)));
        Assert.Throws<NotSupportedException>(() => Translate(h => !(h.Rating > 4 && h.Parking)));
        Assert.Equal("""{"Rating":{"$gt":4}}""", Translate(h => !!(h.Rating > 4)));
    }

    [Fact]
    public void ThrowsForAFilterOnTheVector()
        => Assert.Throws<NotSupportedException>(() => Translate<ArrayHotel>(h => h.Embedding!.Contains(1f)));

    [Fact]
    public void ThrowsForAValueOfAnotherType()
    {
        var values = new List<object> { Guid.NewGuid() };

        Assert.Throws<NotSupportedException>(() => TranslateFilter(h => Enumerable.Contains(h.HotelName!, 'G')));
        Assert.Throws<NotSupportedException>(() => TranslateFilter(h => values.Contains(h.HotelName!)));
    }

    [Fact]
    public void ThrowsForAComparisonOfTwoProperties()
        => Assert.Throws<NotSupportedException>(() => Translate(h => h.Price > h.Price));

    [Fact]
    public void ThrowsForAnEqualityOfTwoProperties()
        => Assert.Throws<NotSupportedException>(() => Translate(h => h.HotelName == h.HotelName));

    [Fact]
    public void ThrowsForAnEqualityWithoutAProperty()
    {
        var name = "Grand";

        Assert.Throws<NotSupportedException>(() => Translate(h => name == "Grand"));
    }

    [Fact]
    public void ThrowsForAnArrayComparedWithAnArray()
    {
        var tags = new List<string> { "pool" };

        Assert.Throws<NotSupportedException>(() => Translate(h => h.Tags == tags));
    }

    [Fact]
    public void ThrowsForAnUnsupportedNodeType()
        => Assert.Throws<NotSupportedException>(() => Translate(h => h.Parking ? h.Rating > 1 : h.Rating < 1));

    [Fact]
    public void ThrowsForAnUnsupportedMethod()
        => Assert.Throws<NotSupportedException>(() => Translate(h => h.Tags!.Any()));

    [Fact]
    public void ThrowsForContainsOverAnArrayPropertyWithAnotherProperty()
        => Assert.Throws<NotSupportedException>(() => Translate(h => h.Tags!.Contains(h.HotelName!)));

    [Fact]
    public void ThrowsForContainsOverAnExpressionThatIsNeitherAPropertyNorAList()
        => Assert.Throws<NotSupportedException>(() => Translate(h => h.Tags!.Concat(new[] { "spa" }).Contains("pool")));

    [Fact]
    public void ThrowsForContainsOverAListOfAConstant()
        => Assert.Throws<NotSupportedException>(() => Translate(h => new[] { "pool" }.Contains("spa")));

    [Fact]
    public void ThrowsForAnInlineArrayWithAnElementThatIsNotAConstant()
        => Assert.Throws<NotSupportedException>(() => Translate(h => new[] { h.HotelId }.Contains(h.HotelName)));

    [Fact]
    public void ThrowsForAnInlineArrayWithALength()
        => Assert.Throws<NotSupportedException>(() => Translate(h => new string[2].Contains(h.HotelName)));

    [Fact]
    public void TranslatesContainsOverACapturedList()
    {
        var names = new List<string> { "a", "b" };

        Assert.Equal("""{"HotelName":{"$in":["a","b"]}}""", Translate(h => names.Contains(h.HotelName!)));
    }

    [Theory]
    [InlineData("not a property")]
    [InlineData("not a method")]
    [InlineData("not contains")]
    [InlineData("not the element")]
    [InlineData("not a list")]
    public void ThrowsForAnyThatIsNotAContainsOverTheElements(string any)
    {
        Expression<Func<ChromaHotel<string>, bool>> filter = any switch
        {
            "not a property" => h => new[] { "pool" }.Any(t => new[] { "pool" }.Contains(t)),
            "not a method" => h => h.Tags!.Any(t => t == "pool"),
            "not contains" => h => h.Tags!.Any(t => t.StartsWith("p")),
            "not the element" => h => h.Tags!.Any(t => new[] { "pool" }.Contains(h.HotelName)),
            "not a list" => h => h.Tags!.Any(t => h.Tags!.Contains(t)),
            _ => throw new ArgumentOutOfRangeException(nameof(any))
        };

        Assert.Throws<NotSupportedException>(() => Translate(filter));
    }

    [Fact]
    public void ThrowsForANullKey()
        => Assert.Throws<NotSupportedException>(() => TranslateFilter(h => new[] { "h1", null }.Contains(h.HotelId)));

    [Fact]
    public void ThrowsForAKeyInAListThatIsNotAConstant()
        => Assert.Throws<NotSupportedException>(() => TranslateFilter(h => h.Tags!.Contains(h.HotelId)));

    [Fact]
    public void ThrowsForATextConditionWithoutAConstant()
        => Assert.Throws<NotSupportedException>(() => TranslateFullText(h => h.Description!.Contains(h.HotelId)));

    [Theory]
    [InlineData("all and")]
    [InlineData("and all")]
    [InlineData("or nothing")]
    [InlineData("nothing and or")]
    [InlineData("and nothing or")]
    public void SimplifiesConditionsThatMatchEverythingOrNothing(string condition)
    {
        var none = Array.Empty<string>();
        Expression<Func<ChromaHotel<string>, bool>> filter = condition switch
        {
            "all and" => h => (true && h.Rating >= 4) || none.Contains(h.HotelName!),
            "and all" => h => (h.Rating >= 4 && true) || none.Contains(h.HotelName!),
            "or nothing" => h => h.Rating >= 4 || none.Contains(h.HotelName!),
            "nothing and or" => h => (none.Contains(h.HotelName!) && h.Parking) || h.Rating >= 4,
            "and nothing or" => h => (h.Parking && none.Contains(h.HotelName!)) || h.Rating >= 4,
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };

        Assert.Equal("""{"Rating":{"$gte":4}}""", Translate(filter));
    }

    [Theory]
    [InlineData("true or")]
    [InlineData("or true")]
    public void TranslatesAnOrWithASideThatMatchesEverythingToNoFilter(string condition)
    {
        Expression<Func<ChromaHotel<string>, bool>> filter = condition == "true or"
            ? h => true || h.Parking
            : h => h.Parking || true;

        AssertMatchesAll(TranslateFilter(filter));
    }

    // The JSON of the client escapes the + of an offset as \u002B, as System.Text.Json does: put it back to read the dates.
    private static string TranslateDated(Expression<Func<DatedHotel, bool>> filter)
        => Translate(filter).ToString().Replace("\\u002B", "+");

    private static ChromaWhereOperator TranslateFullText(Expression<Func<FullTextHotel, bool>> filter)
        => Translate(filter);

    // The filter of a collection of the record: with the property stored as the document, as the collection gives it.
    private static ChromaWhereOperator Translate<TRecord>(Expression<Func<TRecord, bool>> filter)
    {
        var model = ChromaTestModel.Build<TRecord>();
        return new ChromaFilterTranslator().Translate(filter, model, ChromaFieldMapping.GetDocumentProperty(model));
    }

    // The JSON that ChromaDotNet.Client sends in the where clause.
    private static string Translate(Expression<Func<ChromaHotel<string>, bool>> filter)
        => TranslateFilter(filter).ToString()!;

    private static ChromaWhereOperator TranslateFilter(Expression<Func<ChromaHotel<string>, bool>> filter)
        => Translate<ChromaHotel<string>>(filter);

    private static void AssertMatchesAll(ChromaWhereOperator filter)
        => Assert.Same(ChromaWhereOperator.All, filter);
}
