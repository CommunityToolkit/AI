[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\nuget\document-extraction-core'),
    [string] $DotnetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = 'https://github.com/dotnet/extensions.git'
$commit = '5635a6ed7499739e2411f8288e1bd2b340c2c0e2'
$tree = '4ea4fe11e7fcc828012824eece4233a5b5f5e591'
$version = '11.0.0-review.20261106.1'
$requiredSdk = '11.0.100-rc.3.26522.148'
$expectedFrameworks = @('net11.0', 'net10.0', 'net9.0', 'net8.0', 'net462', 'netstandard2.0')
$ids = @(
    'Microsoft.Extensions.AI.Abstractions',
    'Microsoft.Extensions.Documents.Abstractions',
    'Microsoft.Extensions.DocumentExtraction.Abstractions',
    'Microsoft.Extensions.DocumentExtraction'
)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($DotnetPath -ne 'dotnet') {
    $DotnetPath = [IO.Path]::GetFullPath($DotnetPath)
}
$sourceDirectory = Join-Path $OutputDirectory 'source'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (Test-Path (Join-Path $OutputDirectory 'core-bootstrap.json')) {
    throw "Use a new output directory; existing validated provenance will not be overwritten: $OutputDirectory"
}

function Invoke-Checked {
    param([string] $Executable, [string[]] $Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable exited with code $LASTEXITCODE."
    }
}

if (!(Test-Path (Join-Path $sourceDirectory '.git'))) {
    if (Test-Path $sourceDirectory) {
        throw "Refusing to replace an existing non-Git source directory: $sourceDirectory"
    }
    Invoke-Checked git @('-c', 'core.longpaths=true', 'init', '--quiet', $sourceDirectory)
    Invoke-Checked git @('-C', $sourceDirectory, 'remote', 'add', 'origin', $repository)
}

$actualRepository = & git -C $sourceDirectory remote get-url origin
if ($LASTEXITCODE -ne 0 -or $actualRepository -ne $repository) {
    throw "Bootstrap origin must be the canonical repository $repository; found '$actualRepository'."
}
$changes = & git -c core.longpaths=true -C $sourceDirectory status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) {
    throw 'The bootstrap checkout must be unmodified.'
}
Invoke-Checked git @('-c', 'core.longpaths=true', '-C', $sourceDirectory, 'fetch', '--quiet', '--depth=1', 'origin', $commit)
Invoke-Checked git @('-c', 'core.longpaths=true', '-C', $sourceDirectory, 'checkout', '--quiet', '--detach', $commit)
$actualCommit = & git -c core.longpaths=true -C $sourceDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $commit) {
    throw 'Upstream commit verification failed.'
}
$actualTree = & git -c core.longpaths=true -C $sourceDirectory rev-parse 'HEAD^{tree}'
if ($LASTEXITCODE -ne 0 -or $actualTree -ne $tree) {
    throw 'Upstream source-tree verification failed.'
}

Push-Location $sourceDirectory
$previousPackages = $env:NUGET_PACKAGES
$previousHttpCache = $env:NUGET_HTTP_CACHE_PATH
$env:NUGET_PACKAGES = Join-Path $OutputDirectory 'package-cache'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $OutputDirectory 'http-cache'
$evaluations = @()
try {
    $sdk = & $DotnetPath --version
    if ($LASTEXITCODE -ne 0 -or $sdk -ne $requiredSdk) {
        throw "The canonical bootstrap requires SDK $requiredSdk; upstream checkout selected '$sdk'. Supply its dotnet executable with -DotnetPath."
    }
    Write-Host "Packing unmodified canonical source $commit (tree $tree), SDK $sdk, version $version."
    $restoreConfig = Join-Path $sourceDirectory 'NuGet.config'
    foreach ($id in $ids) {
        $project = Join-Path 'src\Libraries' "$id\$id.csproj"
        $evaluation = & $DotnetPath msbuild $project -nologo "-p:PackageVersion=$version" `
            '-getProperty:TargetFrameworks,PackageVersion,Features,ForceLatestDotnetVersions'
        if ($LASTEXITCODE -ne 0) {
            throw "Original owning-project evaluation failed for $id."
        }
        $properties = ($evaluation -join "`n" | ConvertFrom-Json).Properties
        if ($properties.TargetFrameworks -ne ($expectedFrameworks -join ';') -or $properties.PackageVersion -ne $version) {
            throw "Unexpected original owning-project framework/version evaluation for $id."
        }
        $evaluations += @{ id = $id; project = $project; properties = $properties }
        Invoke-Checked $DotnetPath @(
            'pack', $project, '--nologo', '--verbosity', 'quiet',
            "-p:PackageVersion=$version", "-p:PackageOutputPath=$OutputDirectory",
            "-p:RestoreConfigFile=$restoreConfig"
        )
    }
}
finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
    $env:NUGET_HTTP_CACHE_PATH = $previousHttpCache
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packages = @()
foreach ($id in $ids) {
    $path = Join-Path $OutputDirectory "$id.$version.nupkg"
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entry = $archive.Entries | Where-Object FullName -Like '*.nuspec'
        $reader = [IO.StreamReader]::new($entry.Open())
        try {
            [xml] $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        $metadata = $nuspec.package.metadata
        if ($metadata.id -ne $id -or $metadata.version -ne $version -or
            $metadata.repository.commit -ne $commit -or $metadata.repository.url -ne $repository) {
            throw "Unexpected package identity or source commit: $path"
        }
        $actualAssemblies = @($archive.Entries.FullName | Where-Object { $_ -like 'lib/*.dll' } | Sort-Object)
        $expectedAssemblies = @($expectedFrameworks | ForEach-Object { "lib/$_/$id.dll" } | Sort-Object)
        if (@(Compare-Object $expectedAssemblies $actualAssemblies).Count -ne 0) {
            throw "Expected exact original six-framework assemblies in $id; found $($actualAssemblies -join ', ')."
        }
        $packages += @{
            id = $id; version = $version; path = $path
            sha256 = (Get-FileHash $path -Algorithm SHA256).Hash
            assemblies = $actualAssemblies
        }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host "$id $(Get-FileHash $path -Algorithm SHA256 | Select-Object -ExpandProperty Hash)"
}

$changes = & git -c core.longpaths=true -C $sourceDirectory status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) {
    throw 'The canonical source checkout changed while building original projects.'
}
@{
    repository = $repository; commit = $commit; tree = $tree; version = $version
    sdk = $sdk; createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceDirectory = $sourceDirectory; sourceUnmodified = $true
    packageCache = (Join-Path $OutputDirectory 'package-cache')
    restoreConfig = (Join-Path $sourceDirectory 'NuGet.config')
    restoreConfigSha256 = (Get-FileHash (Join-Path $sourceDirectory 'NuGet.config') -Algorithm SHA256).Hash
    overrides = @('PackageVersion', 'PackageOutputPath', 'RestoreConfigFile')
    compilerFeaturesWorkaround = $false
    projects = $evaluations; packages = $packages
} | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'core-bootstrap.json') -Encoding utf8
