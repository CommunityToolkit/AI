// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommunityToolkit.VectorData.SqlServer;

// For mapping string[] properties to SQL Server JSON columns
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
internal partial class SqlServerJsonSerializerContext : JsonSerializerContext;
