// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using DotNet.Testcontainers.Containers;

namespace Weaviate.ConformanceTests.Support.TestContainer;

public class WeaviateContainer(WeaviateConfiguration configuration) : DockerContainer(configuration);
