// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace CommunityToolkit.VectorData.PgVector;

internal sealed class PostgresFilterTranslator : FilterTranslatorBase
{
    private readonly StringBuilder _sql;
    private readonly Expression _preprocessedExpression;
    private int _parameterIndex;

    internal PostgresFilterTranslator(
        CollectionModel model,
        LambdaExpression lambdaExpression,
        int startParamIndex,
        StringBuilder? sql = null)
    {
        Debug.Assert(lambdaExpression.Parameters.Count == 1);
        _sql = sql ?? new();
        _parameterIndex = startParamIndex;

        _preprocessedExpression = PreprocessFilter(lambdaExpression, model, new FilterPreprocessingOptions { SupportsParameterization = true });
    }

    internal StringBuilder Clause => _sql;

    internal List<object> ParameterValues { get; } = [];

    internal void Translate(bool appendWhere)
    {
        if (appendWhere)
        {
            _sql.Append("WHERE ");
        }

        Translate(_preprocessedExpression);
    }

    private void Translate(Expression? node)
    {
        switch (node)
        {
            case BinaryExpression binary:
                TranslateBinary(binary);
                return;

            case ConstantExpression constant:
                TranslateConstant(constant.Value);
                return;

            case QueryParameterExpression { Name: var name, Value: var value }:
                TranslateQueryParameter(value);
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
        // Special handling for null comparisons
        switch (binary.NodeType)
        {
            case ExpressionType.Equal when IsNull(binary.Right):
                _sql.Append('(');
                Translate(binary.Left);
                _sql.Append(" IS NULL)");
                return;
            case ExpressionType.NotEqual when IsNull(binary.Right):
                _sql.Append('(');
                Translate(binary.Left);
                _sql.Append(" IS NOT NULL)");
                return;

            case ExpressionType.Equal when IsNull(binary.Left):
                _sql.Append('(');
                Translate(binary.Right);
                _sql.Append(" IS NULL)");
                return;
            case ExpressionType.NotEqual when IsNull(binary.Left):
                _sql.Append('(');
                Translate(binary.Right);
                _sql.Append(" IS NOT NULL)");
                return;
        }

        _sql.Append('(');
        Translate(binary.Left);

        _sql.Append(binary.NodeType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " <> ",

            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",

            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",

            _ => throw new NotSupportedException("Unsupported binary expression node type: " + binary.NodeType)
        });

        Translate(binary.Right);

        _sql.Append(')');

        static bool IsNull(Expression expression)
            => expression is ConstantExpression { Value: null } or QueryParameterExpression { Value: null };
    }

    private void TranslateConstant(object? value)
    {
        switch (value)
        {
            case byte b:
                _sql.Append(b);
                return;
            case short s:
                _sql.Append(s);
                return;
            case int i:
                _sql.Append(i);
                return;
            case long l:
                _sql.Append(l);
                return;

            case float f:
                _sql.Append(f);
                return;
            case double d:
                _sql.Append(d);
                return;
            case decimal d:
                _sql.Append(d);
                return;

            case string untrustedInput:
                _sql.Append('\'').Append(untrustedInput.Replace("'", "''")).Append('\'');
                return;
            case bool b:
                _sql.Append(b ? "TRUE" : "FALSE");
                return;
            case Guid g:
                _sql.Append('\'').Append(g.ToString()).Append('\'');
                return;

            case DateTime dateTime:
                switch (dateTime.Kind)
                {
                    case DateTimeKind.Utc:
                        _sql.Append('\'').Append(dateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFZ", CultureInfo.InvariantCulture)).Append('\'');
                        return;

                    case DateTimeKind.Unspecified:
                    case DateTimeKind.Local:
                        _sql.Append('\'').Append(dateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFF", CultureInfo.InvariantCulture)).Append('\'');
                        return;

                    default:
                        throw new UnreachableException();
                }

            case DateTimeOffset dateTimeOffset:
                if (dateTimeOffset.Offset != TimeSpan.Zero)
                {
                    throw new NotSupportedException("DateTimeOffset with non-zero offset is not supported with PostgreSQL. Use DateTimeOffset.UtcNow or convert to UTC.");
                }

                _sql.Append('\'').Append(dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFZ", CultureInfo.InvariantCulture)).Append('\'');
                return;

#if NET
            case DateOnly dateOnly:
                _sql.Append('\'').Append(dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\'');
                return;

            case TimeOnly timeOnly:
                _sql.Append('\'').Append(timeOnly.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)).Append('\'');
                return;
#endif

            // Array constants (ARRAY[1, 2, 3])
            case IEnumerable v when v.GetType() is var type && (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)):
                _sql.Append("ARRAY[");

                var arrayIndex = 0;
                foreach (var element in v)
                {
                    if (arrayIndex++ > 0)
                    {
                        _sql.Append(',');
                    }

                    TranslateConstant(element);
                }

                _sql.Append(']');
                return;

            case null:
                _sql.Append("NULL");
                return;

            default:
                throw new NotSupportedException("Unsupported constant type: " + value.GetType().Name);
        }
    }

    private void TranslateMember(MemberExpression memberExpression)
    {
        if (TryBindProperty(memberExpression, out var property))
        {
            GenerateColumn(property);
            return;
        }

        throw new NotSupportedException($"Member access for '{memberExpression.Member.Name}' is unsupported - only member access over the filter parameter are supported");
    }

    private void GenerateColumn(PropertyModel property)
        => _sql.Append('"').Append(property.StorageName.Replace("\"", "\"\"")).Append('"');

    private void TranslateQueryParameter(object? value)
    {
        // For null values, simply inline rather than parameterize; parameterized NULLs require setting NpgsqlDbType which is a bit more complicated,
        // plus in any case equality with NULL requires different SQL (x IS NULL rather than x = y)
        if (value is null)
        {
            _sql.Append("NULL");
        }
        else
        {
            ParameterValues.Add(value);
            _sql.Append('$').Append(_parameterIndex++);
        }
    }

    private void TranslateMethodCall(MethodCallExpression methodCall)
    {
        if (TryBindProperty(methodCall, out var property))
        {
            GenerateColumn(property);
            return;
        }

        switch (methodCall)
        {
            case var _ when TryMatchContains(methodCall, out var source, out var item):
                TranslateContains(source, item);
                return;

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
            // Contains over array column (r => r.Strings.Contains("foo"))
            case var _ when TryBindProperty(source, out _):
                Translate(source);
                _sql.Append(" @> ARRAY[");
                Translate(item);
                _sql.Append(']');
                return;

            // Contains over inline array (r => new[] { "foo", "bar" }.Contains(r.String))
            case NewArrayExpression newArray:
                Translate(item);
                _sql.Append(" IN (");

                var isFirst = true;
                foreach (var element in newArray.Expressions)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        _sql.Append(", ");
                    }

                    Translate(element);
                }

                _sql.Append(')');
                return;

            // Contains over captured array (r => arrayLocalVariable.Contains(r.String))
            case QueryParameterExpression { Value: var value }:
                Translate(item);
                _sql.Append(" = ANY (");
                Translate(source);
                _sql.Append(')');
                return;

            default:
                throw new NotSupportedException("Unsupported Contains expression");
        }
    }

    private void TranslateAny(Expression source, LambdaExpression lambda)
    {
        if (!TryBindProperty(source, out var property)
            || lambda.Body is not MethodCallExpression containsCall
            || !TryMatchContains(containsCall, out var valuesExpression, out var itemExpression))
        {
            throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }

        if (itemExpression != lambda.Parameters[0])
        {
            throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }

        switch (valuesExpression)
        {
            case NewArrayExpression newArray:
            {
                var values = new object?[newArray.Expressions.Count];
                for (var i = 0; i < newArray.Expressions.Count; i++)
                {
                    values[i] = newArray.Expressions[i] switch
                    {
                        ConstantExpression { Value: var v } => v,
                        QueryParameterExpression { Value: var v } => v,
                        _ => throw new NotSupportedException("Unsupported method call: Enumerable.Any")
                    };
                }

                TranslateAnyContainsOverArrayColumn(property, values);
                return;
            }

            case QueryParameterExpression { Value: var value }:
                TranslateAnyContainsOverArrayColumn(property, value);
                return;

            case ConstantExpression { Value: var value }:
                TranslateAnyContainsOverArrayColumn(property, value);
                return;

            default:
                throw new NotSupportedException("Unsupported method call: Enumerable.Any");
        }
    }

    private void TranslateAnyContainsOverArrayColumn(PropertyModel property, object? values)
    {
        // Translate r.Strings.Any(s => array.Contains(s)) to: column && ARRAY[values]
        // The && operator checks if the two arrays have any elements in common
        GenerateColumn(property);
        _sql.Append(" && ");
        TranslateConstant(values);
    }

    private void TranslateUnary(UnaryExpression unary)
    {
        switch (unary.NodeType)
        {
            case ExpressionType.Not:
                if (unary.Operand is BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual } binary)
                {
                    TranslateBinary(
                        Expression.MakeBinary(
                            binary.NodeType is ExpressionType.Equal ? ExpressionType.NotEqual : ExpressionType.Equal,
                            binary.Left,
                            binary.Right));
                    return;
                }

                _sql.Append("(NOT ");
                Translate(unary.Operand);
                _sql.Append(')');
                return;

            case ExpressionType.Convert when Nullable.GetUnderlyingType(unary.Type) == unary.Operand.Type:
                Translate(unary.Operand);
                return;

            case ExpressionType.Convert when TryBindProperty(unary.Operand, out var property) && unary.Type == property.Type:
                GenerateColumn(property);
                return;

            default:
                throw new NotSupportedException("Unsupported unary expression node type: " + unary.NodeType);
        }
    }
}
