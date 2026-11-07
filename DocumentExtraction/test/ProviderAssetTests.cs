// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.Versioning;

namespace CommunityToolkit.DocumentExtraction.Tests;

public sealed class ProviderAssetTests
{
    [Fact]
    public void ProviderAsset_HasExpectedTargetAndExperimentalContract()
    {
        Assembly testAssembly = typeof(ProviderAssetTests).Assembly;
        string providerName = testAssembly.GetName().Name!.Replace(".UnitTests", "", StringComparison.Ordinal);
        Assembly provider = Assembly.Load(providerName);
#if STANDARD_PROVIDER_ASSET
        const string expectedFramework = ".NETStandard,Version=v2.0";
#else
        string expectedFramework = testAssembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
#endif
        Assert.Equal(expectedFramework, provider.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        CustomAttributeData marker = Assert.Single(provider.GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute");
        Assert.Equal("CTDE0001", Assert.Single(marker.ConstructorArguments).Value);
        Assert.Equal("https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics",
            Assert.Single(marker.NamedArguments, argument => argument.MemberName == "UrlFormat").TypedValue.Value);
    }
}
