// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace CommunityToolkit.VectorData.AzureAISearch;

internal class AzureAISearchFilterTranslator : FilterTranslatorBase
{
    private readonly StringBuilder _filter = new();

    private static readonly char[] s_searchInDefaultDelimiter = [' ', ','];

    internal string Translate(LambdaExpression lambdaExpression, CollectionModel model)
    {
        var preprocessedExpression = PreprocessFilter(lambdaExpression, model, new FilterPreprocessingOptions());

        Translate(preprocessedExpression);

        return _filter.ToString();
    }

    private void Translate(Expression? node)
    {
        switch (node)
        {
            case BinaryExpression binary:
                TranslateBinary(binary);
                return;

            case ConstantExpression constant:
                TranslateConstant(constant);
                return;

            case MemberExpression member:
                TranslateMember(member);
                return;

            case MethodCallExpression methodCall:
                TranslateMethodCall(methodCall);
                return;

            case UnaryExpression unary:
                TranslateUnary(unary);
                return;

            default:
                throw new NotSupportedException("Unsupported NodeType in filter: " + node?.NodeType);
        }
    }

    private void TranslateBinary(BinaryExpression binary)
    {
        _filter.Append('(');
        Translate(binary.Left);

        _filter.Append(binary.NodeType switch
        {
            ExpressionType.Equal => " eq ",
            ExpressionType.NotEqual => " ne ",

            ExpressionType.GreaterThan => " gt ",
            ExpressionType.GreaterThanOrEqual => " ge ",
            ExpressionType.LessThan => " lt ",
            ExpressionType.LessThanOrEqual => " le ",

            ExpressionType.AndAlso => " and ",
            ExpressionType.OrElse => " or ",

            _ => throw new NotSupportedException("Unsupported binary expression node type: " + binary.NodeType)
        });

        Translate(binary.Right);
        _filter.Append(')');
    }

    private void TranslateConstant(ConstantExpression constant)
        => GenerateLiteral(constant.Value);

    private void GenerateLiteral(object? value)
    {
        switch (value)
        {
            case byte b:
                _filter.Append(b);
                return;
            case short s:
                _filter.Append(s);
                return;
            case int i:
                _filter.Append(i);
                return;
            case long l:
                _filter.Append(l);
                return;

            case float f:
                _filter.Append(f);
                return;
            case double d:
                _filter.Append(d);
                return;

            case string untrustedInput:
                // This is the only place where we allow untrusted input to be passed in, so we need to quote and escape it.
                _filter.Append('\'').Append(untrustedInput.Replace("'", "''")).Append('\'');
                return;
            case bool b:
                _filter.Append(b ? "true" : "false");
                return;
            case Guid g:
                _filter.Append('\'').Append(g.ToString()).Append('\'');
                return;

            case DateTime d:
                _filter.Append(new DateTimeOffset(d, TimeSpan.Zero).ToString("o"));
                return;
            case DateTimeOffset d:
                _filter.Append(d.ToString("o"));
                return;
#if NET
            case DateOnly d:
                _filter.Append(new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToString("o"));
                return;
#endif

            case Array:
                throw new NotImplementedException();

            case null:
                _filter.Append("null");
                return;

            default:
                throw new NotSupportedException("Unsupported constant type: " + value.GetType().Name);
        }
    }

    private void TranslateMember(MemberExpression memberExpression)
    {
        if (TryBindProperty(memberExpression, out var property))
        {
            // OData identifiers cannot be escaped; storage names are validated during model building.
            _filter.Append(property.StorageName);
            return;
        }

        throw new NotSupportedException($"Member access for '{memberExpression.Member.Name}' is unsupported - only member access over the filter parameter are supported");
    }

    private void TranslateMethodCall(MethodCallExpression methodCall)
    {
        // Dictionary access for dynamic mapping (r => r["SomeString"] == "foo")
        if (TryBindProperty(methodCall, out var property))
        {
            // OData identifiers cannot be escaped; storage names are validated during model building.
            _filter.Append(property.StorageName);
            return;
        }

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
        switch (source)
        {
            // Contains over array field (r => r.Strings.Contains("foo"))
            case var _ when TryBindProperty(source, out _):
                Translate(source);
                _filter.Append("/any(t: t eq ");
                Translate(item);
                _filter.Append(')');
                return;

            // Contains over inline enumerable
            case NewArrayExpression newArray:
                var elements = ExtractArrayValues(newArray);
                _filter.Append("search.in(");
                Translate(item);
                GenerateSearchInValues(elements);
                return;

            case ConstantExpression { Value: IEnumerable enumerable and not string }:
                _filter.Append("search.in(");
                Translate(item);
                GenerateSearchInValues(enumerable);
                return;

            default:
                throw new NotSupportedException("Unsupported Contains expression");
        }
    }

    /// <summary>
    /// Translates an Any() call with a Contains predicate, e.g. r.Strings.Any(s => array.Contains(s)).
    /// This checks whether any element in the array field is contained in the given values.
    /// </summary>
    private void TranslateAny(Expression source, LambdaExpression lambda)
    {
        // We only support the pattern: r.ArrayField.Any(x => values.Contains(x))
        // Translates to: Field/any(t: search.in(t, 'value1, value2, value3'))
        if (!TryBindProperty(source, out var property)
            || lambda.Body is not MethodCallExpression { Method.Name: "Contains" } containsCall
            || !TryMatchContains(containsCall, out var valuesExpression, out var itemExpression))
        {
            throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }

        // Verify that the item is the lambda parameter
        if (itemExpression != lambda.Parameters[0])
        {
            throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }

        // Extract the values and generate the OData filter
        IEnumerable values = valuesExpression switch
        {
            NewArrayExpression newArray => ExtractArrayValues(newArray),
            ConstantExpression { Value: IEnumerable enumerable and not string } => enumerable,
            _ => throw new NotSupportedException("Unsupported method call: Enumerable.Any")
        };

        // Generate: Field/any(t: search.in(t, 'value1, value2, value3'))
        // OData identifiers cannot be escaped; storage names are validated during model building.
        _filter.Append(property.StorageName);
        _filter.Append("/any(t: search.in(t");
        GenerateSearchInValues(values);
        _filter.Append(')');
    }

    /// <summary>
    /// Generates the values portion of a search.in() call, including the comma, quotes, and optional delimiter.
    /// Appends: , 'value1, value2, value3') or , 'value1|value2|value3', '|')
    /// </summary>
    private void GenerateSearchInValues(IEnumerable values)
    {
        _filter.Append(", '");

        string delimiter = ", ";
        var startingPosition = _filter.Length;

RestartLoop:
        var isFirst = true;
        foreach (var element in values)
        {
            if (element is not string stringElement)
            {
                throw new NotSupportedException("search.in() over non-string arrays is not supported");
            }

            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                _filter.Append(delimiter);
            }

            // The default delimiter for search.in() is comma or space.
            // If any element contains a comma or space, we switch to using pipe as the delimiter.
            // If any contains a pipe, we throw (for now).
            switch (delimiter)
            {
                case ", ":
                    if (stringElement.IndexOfAny(s_searchInDefaultDelimiter) > -1)
                    {
                        delimiter = "|";
                        _filter.Length = startingPosition;
                        goto RestartLoop;
                    }

                    break;

                case "|":
                    if (stringElement.Contains('|'))
                    {
                        throw new NotSupportedException("Some elements contain both commas/spaces and pipes, cannot translate to search.in()");
                    }

                    break;
            }

            _filter.Append(stringElement.Replace("'", "''"));
        }

        _filter.Append('\'');

        if (delimiter != ", ")
        {
            _filter
                .Append(", '")
                .Append(delimiter)
                .Append('\'');
        }

        _filter.Append(')');
    }

    private static object?[] ExtractArrayValues(NewArrayExpression newArray)
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

    private void TranslateUnary(UnaryExpression unary)
    {
        switch (unary.NodeType)
        {
            case ExpressionType.Not:
                // Special handling for !(a == b) and !(a != b)
                if (unary.Operand is BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual } binary)
                {
                    TranslateBinary(
                        Expression.MakeBinary(
                            binary.NodeType is ExpressionType.Equal ? ExpressionType.NotEqual : ExpressionType.Equal,
                            binary.Left,
                            binary.Right));
                    return;
                }

                _filter.Append("(not ");
                Translate(unary.Operand);
                _filter.Append(')');
                return;

            // Handle converting non-nullable to nullable; such nodes are found in e.g. r => r.Int == nullableInt
            case ExpressionType.Convert when Nullable.GetUnderlyingType(unary.Type) == unary.Operand.Type:
                Translate(unary.Operand);
                return;

            // Handle convert over member access, for dynamic dictionary access (r => (int)r["SomeInt"] == 8)
            case ExpressionType.Convert when TryBindProperty(unary.Operand, out var property) && unary.Type == property.Type:
                // OData identifiers cannot be escaped; storage names are validated during model building.
                _filter.Append(property.StorageName);
                return;

            default:
                throw new NotSupportedException("Unsupported unary expression node type: " + unary.NodeType);
        }
    }
}
