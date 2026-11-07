[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [Parameter(Mandatory)]
    [string] $NuGetConfig,
    [Parameter(Mandatory)]
    [string] $CorePackageDirectory,
    [Parameter(Mandatory)]
    [string] $OutputDirectory,
    [string] $ConsumerProject = (Join-Path $PSScriptRoot 'DocumentExtraction.PackageSmoke/DocumentExtraction.PackageSmoke.csproj')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$NuGetConfig = [IO.Path]::GetFullPath($NuGetConfig)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ConsumerProject = [IO.Path]::GetFullPath($ConsumerProject)
$CorePackageDirectory = [IO.Path]::GetFullPath($CorePackageDirectory)
$coreReceipt = Get-Content (Join-Path $CorePackageDirectory 'core-bootstrap.json') -Raw | ConvertFrom-Json
$coreIds = @('Microsoft.Extensions.AI.Abstractions', 'Microsoft.Extensions.Documents.Abstractions',
    'Microsoft.Extensions.DocumentExtraction.Abstractions', 'Microsoft.Extensions.DocumentExtraction')
if ($coreReceipt.repository -ne 'https://github.com/dotnet/extensions.git' -or
    $coreReceipt.commit -ne '5635a6ed7499739e2411f8288e1bd2b340c2c0e2' -or
    $coreReceipt.tree -ne '4ea4fe11e7fcc828012824eece4233a5b5f5e591' -or
    $coreReceipt.sdk -ne '11.0.100-rc.3.26522.148' -or !$coreReceipt.sourceUnmodified -or
    $coreReceipt.version -ne '11.0.0-review.20261106.1' -or @($coreReceipt.packages).Count -ne 4 -or
    @(Compare-Object $coreIds @($coreReceipt.packages.id)).Count -ne 0) {
    throw 'Package acceptance requires the exact canonical upstream core bootstrap.'
}
foreach ($package in $coreReceipt.packages) {
    $path = Join-Path $CorePackageDirectory "$($package.id).$($coreReceipt.version).nupkg"
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $package.sha256) {
        throw "Core archive hash differs from bootstrap provenance: $path"
    }
}
if (Test-Path $OutputDirectory) {
    throw "Use a new output directory; existing evidence will not be overwritten: $OutputDirectory"
}
$archives = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'CommunityToolkit.DocumentExtraction.*.nupkg')
if ($archives.Count -eq 0) {
    throw "No extraction packages found in $PackageDirectory"
}
foreach ($archive in $archives) {
    $zip = [IO.Compression.ZipFile]::OpenRead($archive.FullName)
    try {
        $frameworks = @($zip.Entries.FullName |
            Where-Object { $_ -match '^lib/[^/]+/.+' } |
            ForEach-Object { $_.Split('/')[1] } | Sort-Object -Unique)
        if (($frameworks -join ';') -ne 'net10.0;netstandard2.0') {
            throw "Expected only net10.0 and netstandard2.0 lib assets in $($archive.Name); found $($frameworks -join ', ')."
        }
    }
    finally {
        $zip.Dispose()
    }
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
[xml] $config = Get-Content $NuGetConfig -Raw
$source = $config.CreateElement('add')
$source.SetAttribute('key', 'toolkit-provider-packages')
$source.SetAttribute('value', $PackageDirectory)
[void] $config.configuration.packageSources.AppendChild($source)
$mapping = $config.CreateElement('packageSource')
$mapping.SetAttribute('key', 'toolkit-provider-packages')
$pattern = $config.CreateElement('package')
$pattern.SetAttribute('pattern', 'CommunityToolkit.DocumentExtraction.*')
[void] $mapping.AppendChild($pattern)
[void] $config.configuration.packageSourceMapping.AppendChild($mapping)
$consumerConfig = Join-Path $OutputDirectory 'NuGet.config'
$config.Save($consumerConfig)

# Do not allow previously restored preview versions to substitute for these packages.
$env:NUGET_PACKAGES = Join-Path $OutputDirectory 'packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $OutputDirectory 'http-cache'
function Invoke-Logged {
    param([string] $Name, [string[]] $Arguments, [switch] $ExpectExperimentalError)
    $log = Join-Path $OutputDirectory "$Name.log"
    & dotnet @Arguments *> $log
    $code = $LASTEXITCODE
    if ($ExpectExperimentalError) {
        if ($code -eq 0 -or !(Select-String -Path $log -Pattern 'error CTDE0001' -Quiet)) {
            throw "Expected an unsuppressed external CTDE0001 error; inspect $log"
        }
    }
    elseif ($code -ne 0) {
        Get-Content $log -Tail 30
        throw "Package consumer check failed; inspect $log"
    }
    Write-Output "$Name succeeded."
}

Invoke-Logged 'restore' @('restore', $ConsumerProject, '--configfile', $consumerConfig, '--verbosity', 'quiet')
foreach ($pair in @(
    @{ Runtime = 'net10.0'; Asset = 'net10.0' },
    @{ Runtime = 'net10.0'; Asset = 'netstandard2.0' }
)) {
    $name = "$($pair.Runtime)-$($pair.Asset)"
    $common = @($ConsumerProject, '-c', 'Release', '-f', $pair.Runtime, '--no-restore',
        "-p:ProviderAssetFramework=$($pair.Asset)", '--verbosity', 'quiet')
    Invoke-Logged "$name-unsuppressed" (@('build') + $common + @('-p:EnableExperimentalOptIn=false', '--no-incremental')) -ExpectExperimentalError
    Invoke-Logged "$name-opted-in" (@('build') + $common + @('-p:EnableExperimentalOptIn=true', '--no-incremental'))
    Invoke-Logged "$name-executed" @('run', '--project', $ConsumerProject, '-c', 'Release', '-f', $pair.Runtime,
        '--no-build', '--no-restore', "-p:ProviderAssetFramework=$($pair.Asset)", '-p:EnableExperimentalOptIn=true',
        '--', $pair.Asset, (Join-Path $OutputDirectory "$name.json"))
    $receipt = Get-Content (Join-Path $OutputDirectory "$name.json") -Raw | ConvertFrom-Json
    foreach ($binding in $receipt.assemblies) {
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($binding.path)
        $matches = @($archives | Where-Object { $_.Name -like "$assemblyName.*.nupkg" })
        if ($matches.Count -ne 1) {
            throw "Expected one matching provider archive for $assemblyName."
        }
        $zip = [IO.Compression.ZipFile]::OpenRead($matches[0].FullName)
        try {
            $entry = $zip.GetEntry("lib/$($pair.Asset)/$assemblyName.dll")
            if ($null -eq $entry) {
                throw "Missing selected provider asset for $assemblyName."
            }
            $stream = $entry.Open()
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try {
                $hash = [Convert]::ToHexString($algorithm.ComputeHash($stream))
            }
            finally {
                $algorithm.Dispose()
                $stream.Dispose()
            }
            if ($binding.sha256 -ne $hash) {
                throw "Loaded provider DLL does not match selected archive asset: $assemblyName."
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    if (@($receipt.coreAssemblies).Count -ne 4) {
        throw "Expected all four loaded upstream core bindings in $name."
    }
    foreach ($package in $coreReceipt.packages) {
        $bindings = @($receipt.coreAssemblies | Where-Object name -EQ $package.id)
        $zip = [IO.Compression.ZipFile]::OpenRead(
            (Join-Path $CorePackageDirectory "$($package.id).$($coreReceipt.version).nupkg"))
        try {
            $entry = $zip.GetEntry("lib/net10.0/$($package.id).dll")
            if ($null -eq $entry -or $bindings.Count -ne 1) {
                throw "Missing exact modern core assembly binding for $($package.id) in $name."
            }
            $stream = $entry.Open()
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try {
                $hash = [Convert]::ToHexString($algorithm.ComputeHash($stream))
            }
            finally {
                $algorithm.Dispose()
                $stream.Dispose()
            }
            if ($bindings[0].sha256 -ne $hash -or $bindings[0].target -ne '.NETCoreApp,Version=v10.0') {
                throw "Loaded core DLL does not match canonical archive: $($package.id) in $name."
            }
        }
        finally {
            $zip.Dispose()
        }
    }
}
