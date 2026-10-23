// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Linq.Expressions;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace CommunityToolkit.VectorData.Milvus;

internal sealed class MilvusFilterTranslator : FilterTranslatorBase
{
    private string _keyField = null!;

    public string Translate(LambdaExpression filter, CollectionModel model)
    {
        _keyField = model.KeyProperty.StorageName;
        return Translate(PreprocessFilter(filter, model, new FilterPreprocessingOptions()), predicate: true);
    }

    // Primary keys are never null. These are valid Milvus predicates, unlike bare true/false.
    private string ConstantPredicate(bool value) => $"({_keyField} is {(value ? "not " : "")}null)";

    private string Translate(Expression expression, bool predicate = false)
    {
        if (TryBindProperty(expression, out var property))
        {
            if (property is VectorPropertyModel)
            {
                throw new NotSupportedException("Milvus filters cannot reference vector fields.");
            }
            return predicate && property.Type == typeof(bool) ? $"({property.StorageName} == true)" : property.StorageName;
        }
        return expression switch
        {
            ConstantExpression { Value: bool value } when predicate => ConstantPredicate(value),
            ConstantExpression constant => MilvusFieldMapping.Literal(constant.Value),
            UnaryExpression { NodeType: ExpressionType.Convert } convert
                when Nullable.GetUnderlyingType(convert.Type) == convert.Operand.Type => Translate(convert.Operand, predicate),
            UnaryExpression { NodeType: ExpressionType.Not } not => TranslateNegated(not.Operand),
            BinaryExpression binary => TranslateBinary(binary),
            MethodCallExpression call when TryMatchContains(call, out var source, out var item)
                => TryBindProperty(source, out var arrayProperty) && MilvusFieldMapping.GetElementType(arrayProperty.Type) is not null
                    ? TranslateArrayContains(arrayProperty.StorageName, ConstantValue(item))
                    : source switch
                {
                    ConstantExpression { Value: IEnumerable values and not string } => TranslateContains(values, item),
                    NewArrayExpression { NodeType: ExpressionType.NewArrayInit } array
                        => TranslateContains(array.Expressions.Select(ConstantValue).ToArray(), item),
                    _ => throw new NotSupportedException("Milvus Contains requires a constant collection of scalar values."),
                },
            MethodCallExpression { Method.Name: nameof(Enumerable.Any), Arguments: [var source, LambdaExpression lambda] }
                when lambda.Body is MethodCallExpression call && TryMatchContains(call, out var values, out var item)
                    && item == lambda.Parameters[0] && TryBindProperty(source, out var anyProperty)
                    && MilvusFieldMapping.GetElementType(anyProperty.Type) is not null
                => TranslateAny(anyProperty.StorageName, values),
            _ => throw new NotSupportedException($"Unsupported Milvus filter expression: {expression.NodeType}."),
        };
    }

    private string TranslateNegated(Expression expression)
        => expression switch
        {
            UnaryExpression { NodeType: ExpressionType.Not } not => Translate(not.Operand, predicate: true),
            BinaryExpression { NodeType: ExpressionType.AndAlso } and => $"({TranslateNegated(and.Left)} or {TranslateNegated(and.Right)})",
            BinaryExpression { NodeType: ExpressionType.OrElse } or => $"({TranslateNegated(or.Left)} and {TranslateNegated(or.Right)})",
            MethodCallExpression call when TryMatchContains(call, out var source, out var item)
                && TryBindProperty(source, out var property) && MilvusFieldMapping.GetElementType(property.Type) is not null
                => TranslateArrayContains(property.StorageName, ConstantValue(item), negated: true),
            MethodCallExpression { Method.Name: nameof(Enumerable.Any), Arguments: [var source, LambdaExpression lambda] }
                when lambda.Body is MethodCallExpression call && TryMatchContains(call, out var values, out var item)
                    && item == lambda.Parameters[0] && TryBindProperty(source, out var property)
                    && MilvusFieldMapping.GetElementType(property.Type) is not null
                => TranslateAny(property.StorageName, values, negated: true),
            _ => $"(not {Translate(expression, predicate: true)})",
        };

    private string TranslateArrayContains(string name, object? value, bool negated = false)
        => value is null ? ConstantPredicate(negated)
            : GuardArrayMembership(name, $"array_contains({name}, {MilvusFieldMapping.Literal(value)})", negated);

    private static string GuardArrayMembership(string name, string membership, bool negated)
        // Milvus 2.5.27 can retain an unknown result under NOT even with an AND null guard.
        // An explicit null arm keeps the negated predicate true for null arrays.
        => negated ? $"({name} is null or not {membership})" : $"({name} is not null and {membership})";

    private string TranslateAny(string name, Expression values, bool negated = false)
    {
        var elements = values switch
        {
            ConstantExpression { Value: IEnumerable enumerable } => enumerable.Cast<object?>(),
            NewArrayExpression array => array.Expressions.Select(ConstantValue),
            _ => throw new NotSupportedException("Milvus array membership requires constant values."),
        };
        var literals = elements.Where(value => value is not null).Select(MilvusFieldMapping.Literal).ToArray();
        return literals.Length == 0 ? ConstantPredicate(negated)
            : GuardArrayMembership(name, $"array_contains_any({name}, [{string.Join(", ", literals)}])", negated);
    }

    private static object? ConstantValue(Expression expression) => expression switch
    {
        ConstantExpression constant => constant.Value,
        UnaryExpression { NodeType: ExpressionType.Convert } convert
            when Nullable.GetUnderlyingType(convert.Type) == convert.Operand.Type => ConstantValue(convert.Operand),
        _ => throw new NotSupportedException("Milvus inline filter arrays require constant values."),
    };

    private string TranslateContains(IEnumerable values, Expression item)
    {
        if (!TryBindProperty(item, out var property) || property is VectorPropertyModel)
        {
            throw new NotSupportedException("Milvus Contains must test a scalar property.");
        }
        var elements = values.Cast<object?>().ToArray();
        var literals = elements.Where(value => value is not null).Select(MilvusFieldMapping.Literal).ToArray();
        var membership = literals.Length == 0 ? ConstantPredicate(false) : $"({Translate(item)} in [{string.Join(", ", literals)}])";
        if (!property.Type.IsValueType || Nullable.GetUnderlyingType(property.Type) is not null)
        {
            return elements.Contains(null)
                ? $"({property.StorageName} is null or {membership})"
                : literals.Length == 0 ? ConstantPredicate(false) : $"({property.StorageName} is not null and {membership})";
        }
        return membership;
    }

    private static bool IsNull(Expression expression) => expression switch
    {
        ConstantExpression { Value: null } => true,
        UnaryExpression { NodeType: ExpressionType.Convert } convert => IsNull(convert.Operand),
        _ => false,
    };

    private string TranslateBinary(BinaryExpression binary)
    {
        var op = binary.NodeType switch
        {
            ExpressionType.Equal => "==", ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">", ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<", ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "and", ExpressionType.OrElse => "or",
            _ => throw new NotSupportedException($"Unsupported Milvus filter operator: {binary.NodeType}."),
        };
        if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            if (binary.Right is ConstantExpression { Value: null })
            {
                return $"({Translate(binary.Left)} is {(binary.NodeType == ExpressionType.NotEqual ? "not " : "")}null)";
            }
            if (binary.Left is ConstantExpression { Value: null })
            {
                return $"({Translate(binary.Right)} is {(binary.NodeType == ExpressionType.NotEqual ? "not " : "")}null)";
            }
        }
        if (binary.NodeType is ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
            && (IsNull(binary.Left) || IsNull(binary.Right)))
        {
            // Lifted relational operators are false if either operand is null, including
            // captured nullable variables after filter preprocessing.
            return ConstantPredicate(false);
        }
        var logical = binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse;
        var translated = $"({Translate(binary.Left, logical)} {op} {Translate(binary.Right, logical)})";
        if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
        {
            return translated;
        }
        // Milvus uses three-valued null logic. Make comparisons total so C# negation and !=
        // also match rows whose nullable scalar field is null.
        if (TryBindProperty(binary.Left, out var leftField) && IsNullable(leftField.Type)
            && TryBindProperty(binary.Right, out var rightField) && IsNullable(rightField.Type))
        {
            // Two nullable fields: in C#, == matches two nulls, != matches one null, and a relational operator is false with a null.
            var (a, b) = (leftField.StorageName, rightField.StorageName);
            var bothNotNull = $"{a} is not null and {b} is not null and {translated}";
            return binary.NodeType switch
            {
                ExpressionType.Equal => $"(({a} is null and {b} is null) or ({bothNotNull}))",
                ExpressionType.NotEqual => $"(({a} is null and {b} is not null) or ({a} is not null and {b} is null) or ({bothNotNull}))",
                _ => $"({bothNotNull})",
            };
        }
        PropertyModel? nullableProperty = null;
        if (TryBindProperty(binary.Left, out var left) && IsNullable(left.Type)
            && binary.Right is ConstantExpression or UnaryExpression { NodeType: ExpressionType.Convert })
        {
            nullableProperty = left;
        }
        else if (TryBindProperty(binary.Right, out var right) && IsNullable(right.Type)
            && binary.Left is ConstantExpression or UnaryExpression { NodeType: ExpressionType.Convert })
        {
            nullableProperty = right;
        }
        return nullableProperty is null ? translated
            : binary.NodeType == ExpressionType.NotEqual
                ? $"({nullableProperty.StorageName} is null or {translated})"
                : $"({nullableProperty.StorageName} is not null and {translated})";

        static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }
}
