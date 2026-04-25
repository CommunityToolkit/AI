// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.VectorData.SqliteVec;

internal abstract class SqliteWhereCondition(string operand, List<object> values)
{
    public string Operand { get; set; } = operand;

    public List<object> Values { get; set; } = values;

    public string? TableName { get; set; }

    public abstract string BuildQuery(List<string> parameterNames);

    protected string GetOperand() => !string.IsNullOrWhiteSpace(TableName) ?
        $"\"{TableName}\".\"{Operand}\"" :
        $"\"{Operand}\"";
}
