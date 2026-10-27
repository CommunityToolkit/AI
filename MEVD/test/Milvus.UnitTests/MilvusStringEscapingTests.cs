// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace CommunityToolkit.VectorData.Milvus.UnitTests;

public sealed class MilvusStringEscapingTests
{
    [Fact]
    public void StringLiteralsPreserveNonBmpCharactersAndEscapeControlCharacters()
    {
        Assert.Equal("\"emoji \U0001F600\"", MilvusFieldMapping.Literal("emoji \U0001F600"));
        Assert.Equal("\"a\\\"b\\\\c\\n\\r\\t\"", MilvusFieldMapping.Literal("a\"b\\c\n\r\t"));
    }

}
