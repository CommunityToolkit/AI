// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

// The Redis tests are flaky when parallelization is enabled
[assembly: CollectionBehavior(DisableTestParallelization = true)]
