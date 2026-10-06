// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using ChromaDB.Client;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace CommunityToolkit.VectorData.Chroma;

// https://docs.trychroma.com/docs/querying-collections/metadata-filtering
internal sealed class ChromaFilterTranslator : FilterTranslatorBase
{
    private const string TextFilterNotSupported
        = "Chroma filters text with Contains only on the single full-text indexed string property, which is stored as the document.";

    private const string KeyFilterNotSupported
        = "Chroma filters on the key only with == or Contains over a list of keys.";

    private DataPropertyModel? _documentProperty;

    /// <summary>
    /// Translate the filter to the where clause of a Chroma request.
    /// </summary>
    /// <param name="lambdaExpression">The filter.</param>
    /// <param name="model">The model of the collection.</param>
    /// <param name="documentProperty">The property stored as the document, or <see langword="null"/>.</param>
    internal ChromaWhereOperator Translate(LambdaExpression lambdaExpression, CollectionModel model, DataPropertyModel? documentProperty)
    {
        _documentProperty = documentProperty;
        return Translate(PreprocessFilter(lambdaExpression, model, new FilterPreprocessingOptions()));
    }

    private static bool IsStringContains(Expression expression, [NotNullWhen(true)] out Expression? target, [NotNullWhen(true)] out Expression? argument)
    {
        if (expression is MethodCallExpression { Method.Name: nameof(string.Contains), Object: { } instance, Arguments: [var single] } call
            && call.Method.DeclaringType == typeof(string)
            && single.Type == typeof(string))
        {
            target = instance;
            argument = single;
            return true;
        }

        target = null;
        argument = null;
        return false;
    }

    private bool TryBindKey(Expression expression)
        => TryBindProperty(expression, out var property) && property is KeyPropertyModel;

    // The key is the Chroma id of a record, not a metadata field, and the vector is not in the metadata.
    private bool TryBindDataProperty(Expression expression, [NotNullWhen(true)] out DataPropertyModel? property)
    {
        if (!TryBindProperty(expression, out var bound))
        {
            property = null;
            return false;
        }

        property = bound switch
        {
            DataPropertyModel dataProperty => dataProperty,
            KeyPropertyModel => throw new NotSupportedException(KeyFilterNotSupported),
            _ => throw new NotSupportedException("Chroma does not filter on vector properties.")
        };
        return true;
    }

    // negated: whether the node is under an odd number of negations.
    private ChromaWhereOperator Translate(Expression node, bool negated = false)
        => node switch
        {
            BinaryExpression { NodeType: ExpressionType.Equal } equal => TranslateEqual(equal.Left, equal.Right),
            BinaryExpression { NodeType: ExpressionType.NotEqual } notEqual => ChromaWhereOperator.Not(TranslateEqual(notEqual.Left, notEqual.Right)),

            BinaryExpression { NodeType: ExpressionType.GreaterThan } comparison => TranslateComparison(comparison, greater: true, orEqual: false, negated),
            BinaryExpression { NodeType: ExpressionType.GreaterThanOrEqual } comparison => TranslateComparison(comparison, greater: true, orEqual: true, negated),
            BinaryExpression { NodeType: ExpressionType.LessThan } comparison => TranslateComparison(comparison, greater: false, orEqual: false, negated),
            BinaryExpression { NodeType: ExpressionType.LessThanOrEqual } comparison => TranslateComparison(comparison, greater: false, orEqual: true, negated),

            BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso => Translate(andAlso.Left, negated) & Translate(andAlso.Right, negated),
            BinaryExpression { NodeType: ExpressionType.OrElse } orElse => Translate(orElse.Left, negated) | Translate(orElse.Right, negated),

            UnaryExpression { NodeType: ExpressionType.Not } not => ChromaWhereOperator.Not(Translate(not.Operand, !negated)),

            // A bool property as a condition: r => r.Bool.
            Expression when node.Type == typeof(bool) && TryBindDataProperty(node, out var property)
                => ChromaWhereOperator.Equal(property.StorageName, true),

            // r => true matches every record, which is useful for fetching all records, and r => false none.
            ConstantExpression { Value: bool value } => value ? ChromaWhereOperator.All : ChromaWhereOperator.None,

            MethodCallExpression methodCall => TranslateMethodCall(methodCall),

            _ => throw new NotSupportedException("Chroma does not support the following NodeType in filters: " + node.NodeType)
        };

    private ChromaWhereOperator TranslateEqual(Expression left, Expression right)
    {
        // The key of a record is its Chroma id.
        if (TryBindKey(left) && TryGetConstant(right, out var key) || TryBindKey(right) && TryGetConstant(left, out key))
        {
            return ChromaWhereOperator.In(ChromaSearchKeys.Id, ToId(key));
        }

        var (property, value) =
            TryBindDataProperty(left, out var leftProperty) && TryGetConstant(right, out var rightConstant)
                ? (leftProperty, rightConstant)
                : TryBindDataProperty(right, out var rightProperty) && TryGetConstant(left, out var leftConstant)
                    ? (rightProperty, leftConstant)
                    : throw new NotSupportedException("Invalid equality/comparison");

        // A property of a value type that is not nullable is never null: == null matches no record, and != null every record,
        // with no need for the null values that Chroma metadata does not have.
        if (value is null && property.Type.IsValueType && Nullable.GetUnderlyingType(property.Type) is null)
        {
            return ChromaWhereOperator.None;
        }

        return ChromaWhereOperator.Equal(property.StorageName, ToFilterValue(value));
    }

    private ChromaWhereOperator TranslateComparison(BinaryExpression comparison, bool greater, bool orEqual, bool negated)
    {
        // Normalize to property-on-the-left: 5 < r.Int is r.Int > 5.
        DataPropertyModel? property;
        object? value;
        if (TryBindDataProperty(comparison.Left, out property) && TryGetConstant(comparison.Right, out value))
        {
        }
        else if (TryBindDataProperty(comparison.Right, out property) && TryGetConstant(comparison.Left, out value))
        {
            greater = !greater;
        }
        else
        {
            throw new NotSupportedException("Comparison expression not supported by Chroma");
        }

        // A comparison with null is false whatever the value, as in C#: it matches no record.
        if (value is null)
        {
            return ChromaWhereOperator.None;
        }

        // In C# the negation is true for a null value, which the negated comparison of Chroma does not match.
        if (negated && Nullable.GetUnderlyingType(property.Type) is not null)
        {
            throw new NotSupportedException(
                $"Chroma does not support a negated comparison on the nullable property '{property.ModelName}': it would leave out the records where the property is null.");
        }

        if (value is not (int or long or float or double))
        {
            throw new NotSupportedException($"Chroma supports comparisons on numbers only, not on '{value.GetType().Name}'.");
        }

        return (greater, orEqual) switch
        {
            (true, false) => ChromaWhereOperator.GreaterThan(property.StorageName, value),
            (true, true) => ChromaWhereOperator.GreaterThanOrEqual(property.StorageName, value),
            (false, false) => ChromaWhereOperator.LessThan(property.StorageName, value),
            (false, true) => ChromaWhereOperator.LessThanOrEqual(property.StorageName, value)
        };
    }

    // A constant compared with a nullable property is converted to the nullable type, e.g. r => r.NullableInt == 5, and a captured
    // number to the type of the property, e.g. r => r.Double > intVariable, with the implicit conversions of C#.
    private static bool TryGetConstant(Expression expression, out object? value)
    {
        switch (expression)
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;

            case UnaryExpression { NodeType: ExpressionType.Convert, Operand: ConstantExpression constant } convert
                when Nullable.GetUnderlyingType(convert.Type) == constant.Type:
                value = constant.Value;
                return true;

            case UnaryExpression { NodeType: ExpressionType.Convert, Operand: ConstantExpression { Value: { } number } } convert
                when Widen(number, Nullable.GetUnderlyingType(convert.Type) ?? convert.Type) is { } widened:
                value = widened;
                return true;

            default:
                value = null;
                return false;
        }
    }

    // The implicit numeric conversions of C# to the numbers Chroma compares; an explicit cast, which can change the value, is not one.
    private static object? Widen(object number, Type type)
        => type == typeof(double) ? number switch { int i => (double)i, long l => (double)l, float f => (double)f, _ => null }
            : type == typeof(float) ? number switch { int i => (float)i, long l => (float)l, _ => null }
            : type == typeof(long) ? number switch { int i => (long)i, _ => null }
            : null;

    private ChromaWhereOperator TranslateMethodCall(MethodCallExpression methodCall)
        => methodCall switch
        {
            // string.Contains() on the property stored as the document; Chroma has no substring filter on metadata.
            _ when IsStringContains(methodCall, out var target, out var argument) => TranslateTextContains(target, argument),

            // Enumerable.Contains(), List.Contains(), MemoryExtensions.Contains()
            _ when TryMatchContains(methodCall, out var source, out var item)
                => TranslateContains(source, item),

            // Enumerable.Any() with a Contains predicate (r => r.Strings.Any(s => array.Contains(s)))
            { Method.Name: nameof(Enumerable.Any), Arguments: [var anySource, LambdaExpression lambda] } any
                when any.Method.DeclaringType == typeof(Enumerable)
                => TranslateAny(anySource, lambda),

            _ => throw new NotSupportedException($"Unsupported method call: {methodCall.Method.DeclaringType?.Name}.{methodCall.Method.Name}")
        };

    private ChromaWhereOperator TranslateTextContains(Expression target, Expression argument)
    {
        if (_documentProperty is null || !TryBindProperty(target, out var property) || property != _documentProperty)
        {
            throw new NotSupportedException(TextFilterNotSupported);
        }

        if (!TryGetConstant(argument, out var value) || value is not string text)
        {
            throw new NotSupportedException("Chroma filters the text of the document with Contains over a constant string.");
        }

        return ChromaWhereOperator.Document(ChromaWhereDocumentOperator.Contains(text));
    }

    private ChromaWhereOperator TranslateContains(Expression source, Expression item)
    {
        // Contains over a list of keys: new[] { "a", "b" }.Contains(r.Key).
        if (TryBindKey(item))
        {
            if (!TryGetValues(source, out var keys))
            {
                throw new NotSupportedException(KeyFilterNotSupported);
            }

            return ChromaWhereOperator.In(ChromaSearchKeys.Id, keys.Cast<object?>().Select(ToId).Distinct().ToArray<object>());
        }

        // Contains over an array property: r.Strings.Contains("a").
        if (TryBindDataProperty(source, out var property))
        {
            if (!TryGetConstant(item, out var value))
            {
                throw new NotSupportedException("Chroma supports Contains over an array property only with a constant item.");
            }

            return ChromaWhereOperator.Contains(property.StorageName, ToFilterValue(value));
        }

        // Contains over a list of values: new[] { "a", "b" }.Contains(r.String); over no values it matches no record.
        if (TryGetValues(source, out var elements))
        {
            if (!TryBindDataProperty(item, out var itemProperty))
            {
                throw new NotSupportedException("Unsupported item type in Contains");
            }

            return ChromaWhereOperator.In(itemProperty.StorageName, elements.Cast<object?>().Select(ToFilterValue).Distinct().ToArray());
        }

        throw new NotSupportedException("Unsupported Contains expression");
    }

    // r.Strings.Any(s => array.Contains(s)) is true when the field contains at least one of the values; over no values it matches no record.
    private ChromaWhereOperator TranslateAny(Expression source, LambdaExpression lambda)
    {
        if (!TryBindDataProperty(source, out var property)
            || lambda.Body is not MethodCallExpression containsCall
            || !TryMatchContains(containsCall, out var valuesExpression, out var itemExpression)
            || itemExpression != lambda.Parameters[0]
            || !TryGetValues(valuesExpression, out var values))
        {
            throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }

        return values.Cast<object?>()
            .Select(ToFilterValue)
            .Distinct()
            .Aggregate(ChromaWhereOperator.None, (any, value) => any | ChromaWhereOperator.Contains(property.StorageName, value));
    }

    // The values of a list in a filter: an inline array, or a captured list or array, which the preprocessing makes a constant.
    private static bool TryGetValues(Expression expression, [NotNullWhen(true)] out IEnumerable? values)
    {
        values = expression switch
        {
            NewArrayExpression newArray => GetInlineArrayElements(newArray),
            ConstantExpression { Value: IEnumerable enumerable and not string } => enumerable,
            _ => null
        };
        return values is not null;
    }

    // The elements of an inline array: new[] { "a", "b" }, or none for new string[0], whose only expression is its length.
    private static object?[] GetInlineArrayElements(NewArrayExpression newArray)
        => newArray switch
        {
            { NodeType: ExpressionType.NewArrayInit } => newArray.Expressions
                .Select(element => element is ConstantExpression { Value: var value }
                    ? value
                    : throw new NotSupportedException("Inline array elements must be constants"))
                .ToArray(),
            { NodeType: ExpressionType.NewArrayBounds, Expressions: [ConstantExpression { Value: 0 }] } => [],
            _ => throw new NotSupportedException("Unsupported inline array")
        };

    private static string ToId(object? key)
        => key is null
            ? throw new NotSupportedException("Chroma does not support filtering on a null key.")
            : ChromaFieldMapping.ToId(key);

    private static object ToFilterValue(object? value)
    {
        switch (value)
        {
            case null:
                throw new NotSupportedException("Chroma does not support filtering on null values.");
            case IEnumerable and not string:
                throw new NotSupportedException("Chroma does not support comparing an array property with an array.");
        }

        try
        {
            return ChromaMetadataConvert.ToMetadataValue(value)!;
        }
        catch (ArgumentException exception)
        {
            throw new NotSupportedException($"Chroma does not support filtering on values of type '{value.GetType().Name}'.", exception);
        }
    }
}
