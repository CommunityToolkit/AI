// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace CommunityToolkit.VectorData.SqliteVec;

internal sealed class SqliteFilterTranslator : FilterTranslatorBase
{
    private readonly StringBuilder _sql;
    private readonly Expression _preprocessedExpression;
    private readonly Dictionary<string, object> _parameters = [];

    internal SqliteFilterTranslator(CollectionModel model, LambdaExpression lambdaExpression)
    {
        Debug.Assert(lambdaExpression.Parameters.Count == 1);
        _sql = new();

        _preprocessedExpression = PreprocessFilter(lambdaExpression, model, new FilterPreprocessingOptions { SupportsParameterization = true });
    }

    internal StringBuilder Clause => _sql;

    internal Dictionary<string, object> Parameters => _parameters;

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
                // Microsoft.Data.Sqlite writes GUIDs as upper-case strings, align our constant formatting with that.
                _sql.Append('\'').Append(g.ToString().ToUpperInvariant()).Append('\'');
                return;

            case DateTime dateTime:
                _sql.Append('\'').Append(dateTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", System.Globalization.CultureInfo.InvariantCulture)).Append('\'');
                return;
            case DateTimeOffset dateTimeOffset:
                _sql.Append('\'').Append(dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", System.Globalization.CultureInfo.InvariantCulture)).Append('\'');
                return;
#if NET
            case DateOnly dateOnly:
                _sql.Append('\'').Append(dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append('\'');
                return;
            case TimeOnly timeOnly:
                _sql.Append('\'').Append(timeOnly.ToString("HH:mm:ss.FFFFFFF", System.Globalization.CultureInfo.InvariantCulture)).Append('\'');
                return;
#endif

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
        // For null values, simply inline rather than parameterize
        if (value is null)
        {
            _sql.Append("NULL");
        }
        else
        {
            int index = _sql.Length;
            _sql.Append('@').Append(_parameters.Count + 1);
            string paramName = _sql.ToString(index, _sql.Length - index);
            _parameters.Add(paramName, value);
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
            // TODO: support Contains over array fields (#10343)
            case var _ when TryBindProperty(source, out _):
                throw new NotSupportedException("Unsupported Contains expression");

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
                if (value is not IEnumerable elements)
                {
                    throw new NotSupportedException("Unsupported Contains expression");
                }

                Translate(item);
                _sql.Append(" IN (");

                isFirst = true;
                foreach (var element in elements)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        _sql.Append(", ");
                    }

                    TranslateConstant(element);
                }

                _sql.Append(')');
                return;

            default:
                throw new NotSupportedException("Unsupported Contains expression");
        }
    }

    // TODO: support Any over array fields (#10343)
    private void TranslateAny(Expression source, LambdaExpression lambda)
        => throw new NotSupportedException("Unsupported method call: Enumerable.Any");

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
