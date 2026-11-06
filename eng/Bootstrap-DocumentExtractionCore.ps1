[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\nuget\document-extraction-core'),
    [string] $DotnetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$commit = 'c03481c76b63c6820e1de6ed9da09d5ff1e9f495'
$tree = 'f2e9d521223db1cf33fa8bcec5667da26b92cc49'
$version = '10.10.0-review.20261104.4'
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
    Invoke-Checked git @('-C', $sourceDirectory, 'remote', 'add', 'origin', 'https://github.com/luisquintanilla/extensions.git')
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
try {
    $sdk = & $DotnetPath --version
    if ($LASTEXITCODE -ne 0 -or $sdk -ne '10.0.401') {
        throw "The verified bootstrap requires SDK 10.0.401; upstream checkout selected '$sdk'. Supply its dotnet executable with -DotnetPath."
    }
    Write-Host "Packing unmodified public source $commit (tree $tree), SDK $sdk, version $version."
    $restoreConfig = Join-Path $sourceDirectory 'NuGet.config'
    foreach ($id in $ids) {
        $project = Join-Path 'src\Libraries' "$id\$id.csproj"
        Invoke-Checked $DotnetPath @(
            'pack', $project, '--nologo', '--verbosity', 'quiet',
            "-p:PackageVersion=$version", '-p:VersionSuffix=review.20261104.4',
            '-p:Features=', "-p:PackageOutputPath=$OutputDirectory",
            "-p:RestoreConfigFile=$restoreConfig"
        )
    }
}
finally {
    Pop-Location
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
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
        if ($metadata.id -ne $id -or $metadata.version -ne $version -or $metadata.repository.commit -ne $commit) {
            throw "Unexpected package identity or source commit: $path"
        }
        $assemblies = @($archive.Entries | Where-Object FullName -Like 'lib/*.dll')
        if ($assemblies.Count -ne 5) {
            throw "Expected five actual upstream TFM assemblies in $id."
        }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host "$id $(Get-FileHash $path -Algorithm SHA256 | Select-Object -ExpandProperty Hash)"
}
