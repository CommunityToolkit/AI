[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CorePackageDirectory,
    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$feed = [IO.Path]::GetFullPath($CorePackageDirectory)
if (!(Test-Path $feed -PathType Container)) {
    throw "The validated core package directory does not exist: $feed"
}
$config = [xml] @'
<configuration>
  <packageSources>
    <clear />
    <add key="core-source-bootstrap" value="" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="core-source-bootstrap">
      <package pattern="Microsoft.Extensions.Documents.Abstractions" />
      <package pattern="Microsoft.Extensions.DocumentExtraction*" />
      <package pattern="Microsoft.Extensions.AI.Abstractions" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
'@
$config.configuration.packageSources.add[0].value = $feed
$config.Save([IO.Path]::GetFullPath($OutputPath))
