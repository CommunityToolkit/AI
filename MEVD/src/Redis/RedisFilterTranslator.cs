// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace CommunityToolkit.VectorData.Redis;

internal class RedisFilterTranslator : FilterTranslatorBase
{
    private readonly StringBuilder _filter = new();

    internal string Translate(LambdaExpression lambdaExpression, CollectionModel model)
    {
        // Redis doesn't seem to have a native way of expressing "always true" filters; since this scenario is important for fetching
        // all records (via GetAsync with filter), we special-case and support it here. Note that false isn't supported (useless),
        // nor is 'x && true'.
        if (lambdaExpression.Body is ConstantExpression { Value: true })
        {
            return "*";
        }

        var preprocessedExpression = PreprocessFilter(lambdaExpression, model, new FilterPreprocessingOptions());

        Translate(preprocessedExpression);
        return _filter.ToString();
    }

    private void Translate(Expression? node)
    {
        switch (node)
        {
            case BinaryExpression
            {
                NodeType: ExpressionType.Equal or ExpressionType.NotEqual
                or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
            } binary:
                TranslateEqualityComparison(binary);
                return;

            case BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso:
                // https://redis.io/docs/latest/develop/interact/search-and-query/query/combined/#and
                _filter.Append('(');
                Translate(andAlso.Left);
                _filter.Append(' ');
                Translate(andAlso.Right);
                _filter.Append(')');
                return;

            case BinaryExpression { NodeType: ExpressionType.OrElse } orElse:
                // https://redis.io/docs/latest/develop/interact/search-and-query/query/combined/#or
                _filter.Append('(');
                Translate(orElse.Left);
                _filter.Append(" | ");
                Translate(orElse.Right);
                _filter.Append(')');
                return;

            case UnaryExpression { NodeType: ExpressionType.Not } not:
                TranslateNot(not.Operand);
                return;

            // Handle converting non-nullable to nullable; such nodes are found in e.g. r => r.Int == nullableInt
            case UnaryExpression { NodeType: ExpressionType.Convert } convert when Nullable.GetUnderlyingType(convert.Type) == convert.Operand.Type:
                Translate(convert.Operand);
                return;

            // MemberExpression is generally handled within e.g. TranslateEqual; this is used to translate direct bool inside filter (e.g. Filter => r => r.Bool)
            case MemberExpression member when member.Type == typeof(bool) && TryBindProperty(member, out _):
            {
                TranslateEqualityComparison(Expression.Equal(member, Expression.Constant(true)));
                return;
            }

            case MethodCallExpression methodCall:
                TranslateMethodCall(methodCall);
                return;

            default:
                throw new NotSupportedException("Redis does not support the following NodeType in filters: " + node?.NodeType);
        }
    }

    private void TranslateEqualityComparison(BinaryExpression binary)
    {
        if (!TryProcessEqualityComparison(binary.Left, binary.Right) && !TryProcessEqualityComparison(binary.Right, binary.Left))
        {
            throw new NotSupportedException("Binary expression not supported by Redis");
        }

        bool TryProcessEqualityComparison(Expression first, Expression second)
        {
            if (TryBindProperty(first, out var property) && second is ConstantExpression { Value: var constantValue })
            {
                // Numeric negation has a special syntax (!=), for the rest we nest in a NOT
                if (binary.NodeType is ExpressionType.NotEqual && constantValue is not (int or long or float or double))
                {
                    TranslateNot(Expression.Equal(first, second));
                    return true;
                }

                // Redis field names cannot be escaped in all contexts; storage names are validated during model building.
                // https://redis.io/docs/latest/develop/interact/search-and-query/query/exact-match
                _filter.Append('@').Append(property.StorageName);

                _filter.Append(
                    binary.NodeType switch
                    {
                        ExpressionType.Equal when constantValue is byte or short or int or long or float or double => $" == {constantValue}",
                        ExpressionType.Equal when constantValue is string stringValue
                            => $$""":{"{{SanitizeStringConstant(stringValue)}}"}""",
                        ExpressionType.Equal when constantValue is null => throw new NotSupportedException("Null value type not supported"), // TODO

                        ExpressionType.NotEqual when constantValue is int or long or float or double => $" != {constantValue}",
                        ExpressionType.NotEqual => throw new InvalidOperationException("Unreachable"), // Handled above

                        ExpressionType.GreaterThan => $" > {constantValue}",
                        ExpressionType.GreaterThanOrEqual => $" >= {constantValue}",
                        ExpressionType.LessThan => $" < {constantValue}",
                        ExpressionType.LessThanOrEqual => $" <= {constantValue}",

                        _ => throw new InvalidOperationException("Unsupported equality/comparison")
                    });

                return true;
            }

            return false;
        }
    }

    private void TranslateNot(Expression expression)
    {
        // https://redis.io/docs/latest/develop/interact/search-and-query/query/combined/#not
        _filter.Append("(-");
        Translate(expression);
        _filter.Append(')');
    }

    private void TranslateMethodCall(MethodCallExpression methodCall)
    {
        switch (methodCall)
        {
            // Enumerable.Contains(), List.Contains(), MemoryExtensions.Contains()
            case var _ when TryMatchContains(methodCall, out var source, out var item):
                TranslateContains(source, item);
                return;

            // Enumerable.Any() with a Contains predicate (r => r.Strings.Any(s => array.Contains(s)))
            case { Method.Name: nameof(Enumerable.Any), Arguments: [var anySource, LambdaExpression lambda] } any
                when any.Method.DeclaringType == typeof(Enumerable):
                TranslateAny(anySource, lambda);
                return;

            default:
                throw new NotSupportedException($"Unsupported method call: {methodCall.Method.DeclaringType?.Name}.{methodCall.Method.Name}");
        }
    }

    private void TranslateContains(Expression source, Expression item)
    {
        // Contains over tag field
        if (TryBindProperty(source, out var property) && item is ConstantExpression { Value: string stringConstant })
        {
            // Redis field names cannot be escaped in all contexts; storage names are validated during model building.
            _filter
                .Append('@')
                .Append(property.StorageName)
                .Append(":{\"")
                .Append(SanitizeStringConstant(stringConstant))
                .Append("\"}");
            return;
        }

        throw new NotSupportedException("Contains supported only over tag field");
    }

    /// <summary>
    /// Translates an Any() call with a Contains predicate, e.g. r.Strings.Any(s => array.Contains(s)).
    /// This checks whether any element in the array field is contained in the given values.
    /// </summary>
    private void TranslateAny(Expression source, LambdaExpression lambda)
    {
        // We only support the pattern: r.ArrayField.Any(x => values.Contains(x))
        // Translates to: @Field:{value1 | value2 | value3}
        if (!TryBindProperty(source, out var property)
            || lambda.Body is not MethodCallExpression containsCall
            || !TryMatchContains(containsCall, out var valuesExpression, out var itemExpression))
        {
            throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }

        // Verify that the item is the lambda parameter
        if (itemExpression != lambda.Parameters[0])
        {
            throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }

        // Extract the values
        IEnumerable values = valuesExpression switch
        {
            NewArrayExpression newArray => ExtractArrayValues(newArray),
            ConstantExpression { Value: IEnumerable enumerable and not string } => enumerable,
            _ => throw new NotSupportedException("Unsupported method call: Enumerable.Any")
        };

        // Generate: @Field:{value1 | value2 | value3}
        _filter
            .Append('@')
            .Append(property.StorageName)
            .Append(":{");

        var isFirst = true;
        foreach (var element in values)
        {
            if (element is not string stringElement)
            {
                throw new NotSupportedException("Any with Contains over non-string arrays is not supported");
            }

            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                _filter.Append(" | ");
            }

            _filter.Append('"').Append(SanitizeStringConstant(stringElement)).Append('"');
        }

        _filter.Append('}');

        static object?[] ExtractArrayValues(NewArrayExpression newArray)
        {
            var result = new object?[newArray.Expressions.Count];
            for (var i = 0; i < newArray.Expressions.Count; i++)
            {
                if (newArray.Expressions[i] is not ConstantExpression { Value: var elementValue })
                {
                    throw new NotSupportedException("Invalid element in array");
                }

                result[i] = elementValue;
            }

            return result;
        }
    }

    private static string SanitizeStringConstant(string value)
#if NET
        => value.Replace("\"", "\\\"", StringComparison.Ordinal);
#else
        => value.Replace("\"", "\\\"");
#endif
}
