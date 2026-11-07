[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [Parameter(Mandatory)]
    [string] $NuGetConfig,
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
}
