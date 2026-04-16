// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.SqliteVec;

internal sealed class SqliteWhereInCondition(string operand, List<object> values)
    : SqliteWhereCondition(operand, values)
{
    public override string BuildQuery(List<string> parameterNames)
    {
        const string InOperator = "IN";

        if (parameterNames.Count == 0) { throw new ArgumentException($"Cannot build '{nameof(SqliteWhereInCondition)}' condition without parameter names."); }

        return $"{GetOperand()} {InOperator} ({string.Join(", ", parameterNames)})";
    }
}
