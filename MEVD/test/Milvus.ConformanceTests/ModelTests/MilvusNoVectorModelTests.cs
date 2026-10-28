// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Milvus.ConformanceTests.Support;
using CommunityToolkit.VectorData.Milvus;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;
using Xunit;

namespace Milvus.ConformanceTests.ModelTests;

public class MilvusNoVectorModelTests(MilvusNoVectorModelTests.Fixture fixture) : NoVectorModelTests<long>(fixture), IClassFixture<MilvusNoVectorModelTests.Fixture>, IAsyncLifetime
{
    ValueTask IAsyncLifetime.InitializeAsync() => default;

    ValueTask IAsyncDisposable.DisposeAsync() => default;

    public override Task GetAsync_single_record(bool includeVectors) { fixture.AssertUnsupported(); return Task.CompletedTask; }

    public override Task Insert_single_record() { fixture.AssertUnsupported(); return Task.CompletedTask; }

    public override Task Delete_single_record() { fixture.AssertUnsupported(); return Task.CompletedTask; }

    public new class Fixture : NoVectorModelTests<long>.Fixture
    {
        public override TestStore TestStore => MilvusTestStore.Instance;

        public override async ValueTask InitializeAsync() => await TestStore.ReferenceCountingStartAsync();

        public override async ValueTask DisposeAsync() => await TestStore.ReferenceCountingStopAsync();

        public void AssertUnsupported()
        {
            using var store = new MilvusVectorStore(MilvusTestStore.Instance.Client, ownsClient: false);
            Assert.Throws<NotSupportedException>(() => store.GetDynamicCollection("unsupported", CreateRecordDefinition()));
        }
    }
}

