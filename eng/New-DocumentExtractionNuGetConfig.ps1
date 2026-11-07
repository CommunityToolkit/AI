[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CorePackageDirectory,
    [Parameter(Mandatory)]
    [string] $OutputPath,
    [string] $DocumentIntelligencePackageSource = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'
$feed = [IO.Path]::GetFullPath($CorePackageDirectory)
if (!(Test-Path $feed -PathType Container)) {
    throw "The validated core package directory does not exist: $feed"
}
$receiptPath = Join-Path $feed 'core-bootstrap.json'
if (!(Test-Path $receiptPath -PathType Leaf)) {
    throw "Canonical source bootstrap provenance is missing: $receiptPath"
}
$receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
$ids = @('Microsoft.Extensions.AI.Abstractions', 'Microsoft.Extensions.Documents.Abstractions',
    'Microsoft.Extensions.DocumentExtraction.Abstractions', 'Microsoft.Extensions.DocumentExtraction')
if ($receipt.repository -ne 'https://github.com/dotnet/extensions.git' -or
    $receipt.commit -ne '5635a6ed7499739e2411f8288e1bd2b340c2c0e2' -or
    $receipt.tree -ne '4ea4fe11e7fcc828012824eece4233a5b5f5e591' -or
    $receipt.version -ne '11.0.0-review.20261106.1' -or
    $receipt.sdk -ne '11.0.100-rc.3.26522.148' -or !$receipt.sourceUnmodified -or
    @($receipt.packages).Count -ne $ids.Count) {
    throw 'Core feed provenance does not match the pinned canonical upstream build.'
}
foreach ($id in $ids) {
    $packages = @($receipt.packages | Where-Object id -EQ $id)
    $path = Join-Path $feed "$id.$($receipt.version).nupkg"
    if ($packages.Count -ne 1 -or !(Test-Path $path -PathType Leaf) -or
        (Get-FileHash $path -Algorithm SHA256).Hash -ne $packages[0].sha256) {
        throw "Canonical core archive is missing or changed: $path"
    }
}
$config = [xml] @'
<configuration>
  <packageSources>
    <clear />
    <add key="core-source-bootstrap" value="" />
    <add key="dotnet-public" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json" />
    <add key="document-intelligence-dependency" value="" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="core-source-bootstrap">
      <package pattern="Microsoft.Extensions.Documents.Abstractions" />
      <package pattern="Microsoft.Extensions.DocumentExtraction*" />
      <package pattern="Microsoft.Extensions.AI.Abstractions" />
    </packageSource>
    <packageSource key="dotnet-public">
      <package pattern="*" />
    </packageSource>
    <packageSource key="document-intelligence-dependency">
      <package pattern="Azure.AI.DocumentIntelligence" />
    </packageSource>
  </packageSourceMapping>
</configuration>
'@
$config.configuration.packageSources.add[0].value = $feed
$config.configuration.packageSources.add[2].value = $DocumentIntelligencePackageSource
$config.Save([IO.Path]::GetFullPath($OutputPath))
