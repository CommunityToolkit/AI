// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AzureDocumentDB.ConformanceTests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace AzureDocumentDB.ConformanceTests;

public class AzureDocumentDBFilterTests(AzureDocumentDBFilterTests.Fixture fixture)
    : FilterTests<string>(fixture), IClassFixture<AzureDocumentDBFilterTests.Fixture>
{
    // Specialized MongoDB syntax for NOT over Contains ($nin)
    [Fact]
    public virtual Task Not_over_Contains()
        => this.TestFilterAsync(
            r => !new[] { 8, 10 }.Contains(r.Int),
            r => !new[] { 8, 10 }.Contains((int)r["Int"]!));

    #region Null checking

    // MongoDB currently doesn't support null checking ({ "Foo" : null }) in vector search pre-filters
    public override Task Equal_with_null_reference_type()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Equal_with_null_reference_type());

    public override Task Equal_with_null_captured()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Equal_with_null_captured());

    public override Task NotEqual_with_null_reference_type()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.NotEqual_with_null_reference_type());

    public override Task NotEqual_with_null_captured()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.NotEqual_with_null_captured());

    public override Task Equal_int_property_with_null_nullable_int()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Equal_int_property_with_null_nullable_int());

    #endregion

    #region Not

    // MongoDB currently doesn't support NOT in vector search pre-filters
    // (https://www.mongodb.com/docs/atlas/atlas-vector-search/vector-search-stage/#atlas-vector-search-pre-filter)
    public override Task Not_over_And()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Not_over_And());

    public override Task Not_over_Or()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Not_over_Or());

    #endregion

    public override Task Contains_over_field_string_array()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Contains_over_field_string_array());

    public override Task Contains_over_field_string_List()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Contains_over_field_string_List());

    #region Enumerable.Any / Contains

    // AzureDocumentDB filter translator doesn't support Enumerable.Any or Enumerable.Contains/MemoryExtensions.Contains
    public override Task Any_with_Contains_over_inline_string_array()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Any_with_Contains_over_inline_string_array());

    public override Task Any_with_Contains_over_captured_string_array()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Any_with_Contains_over_captured_string_array());

    public override Task Any_with_Contains_over_captured_string_list()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Any_with_Contains_over_captured_string_list());

    public override Task Any_over_List_with_Contains_over_captured_string_array()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Any_over_List_with_Contains_over_captured_string_array());

    public override Task Contains_with_Enumerable_Contains()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Contains_with_Enumerable_Contains());

#if NET
    public override Task Contains_with_MemoryExtensions_Contains()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Contains_with_MemoryExtensions_Contains());

    public override Task Contains_with_MemoryExtensions_Contains_with_null_comparer()
        => Assert.ThrowsAsync<NotSupportedException>(() => base.Contains_with_MemoryExtensions_Contains_with_null_comparer());
#endif

    #endregion

    public new class Fixture : FilterTests<string>.Fixture
    {
        public override TestStore TestStore => AzureDocumentDBTestStore.Instance;

        protected override string IndexKind => Microsoft.Extensions.VectorData.IndexKind.IvfFlat;
        protected override string DistanceFunction => Microsoft.Extensions.VectorData.DistanceFunction.CosineDistance;
    }
}
