// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Grpc.Core;
using Microsoft.Extensions.VectorData;
using Milvus.Client;

namespace CommunityToolkit.VectorData.Milvus;

// Milvus.Client reports server errors as MilvusException, gRPC errors as RpcException, and a wait that runs out of time,
// such as the wait for a collection to load, as TimeoutException.
internal static class MilvusErrorHandler
{
    public static Task RunAsync(VectorStoreCollectionMetadata metadata, string operationName, Func<Task> operation)
        => VectorStoreErrorHandler.RunOperationAsync<MilvusException>(metadata, operationName,
            () => VectorStoreErrorHandler.RunOperationAsync<RpcException>(metadata, operationName,
                () => VectorStoreErrorHandler.RunOperationAsync<TimeoutException>(metadata, operationName, operation)));

    public static Task<T> RunAsync<T>(VectorStoreCollectionMetadata metadata, string operationName, Func<Task<T>> operation)
        => VectorStoreErrorHandler.RunOperationAsync<T, MilvusException>(metadata, operationName,
            () => VectorStoreErrorHandler.RunOperationAsync<T, RpcException>(metadata, operationName,
                () => VectorStoreErrorHandler.RunOperationAsync<T, TimeoutException>(metadata, operationName, operation)));
}
