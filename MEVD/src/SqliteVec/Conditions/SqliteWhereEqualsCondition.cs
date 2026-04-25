// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.SqliteVec;

internal sealed class SqliteWhereEqualsCondition(string operand, object value)
    : SqliteWhereCondition(operand, [value])
{
    public override string BuildQuery(List<string> parameterNames)
    {
        const string EqualsOperator = "=";

        if (parameterNames.Count == 0) { throw new ArgumentException($"Cannot build '{nameof(SqliteWhereEqualsCondition)}' condition without parameter name."); }

        return $"{GetOperand()} {EqualsOperator} {parameterNames[0]}";
    }
}
