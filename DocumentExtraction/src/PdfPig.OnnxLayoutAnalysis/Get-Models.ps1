#requires -Version 7.0

param(
    [Parameter(Mandatory = $true)]
    [string] $ModelsDirectory,

    [switch] $VerifyOnly
)

$ErrorActionPreference = 'Stop'

function Assert-ModelFile {
    param(
        [string] $Path,
        [hashtable] $Model
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        (Get-Item -LiteralPath $Path).Length -ne $Model.Bytes -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Model.Hash) {
        throw "Unexpected model artifact: $Path. Expected $($Model.Bytes) bytes and SHA256 $($Model.Hash). Existing files are not overwritten."
    }
}

function Save-ModelFile {
    param(
        [string] $Directory,
        [hashtable] $Model,
        [switch] $VerifyOnly
    )

    $destination = Join-Path $Directory $Model.File
    if (Test-Path -LiteralPath $destination) {
        Assert-ModelFile -Path $destination -Model $Model
    }
    elseif ($VerifyOnly) {
        throw "Missing model artifact: $destination. Verification-only mode does not download files."
    }
    else {
        $temporary = Join-Path $Directory ".$($Model.File).$([Guid]::NewGuid().ToString('N')).partial"
        try {
            Invoke-WebRequest -Uri $Model.Uri -OutFile $temporary
            Assert-ModelFile -Path $temporary -Model $Model
            [System.IO.File]::Move($temporary, $destination)
        }
        finally {
            if (Test-Path -LiteralPath $temporary -PathType Leaf) {
                Remove-Item -LiteralPath $temporary -Force
            }
        }
    }

    Write-Output "Verified $destination"
}

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'models.json') -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.SchemaVersion -ne 1 -or $manifest.Models.Count -ne 4) {
    throw 'Expected the version 1 four-model sample manifest.'
}

$models = $manifest.Models
$roles = @('Layout', 'OcrDetector', 'OcrRecognizer', 'TableStructure')
if (@($models.Role | Sort-Object -Unique).Count -ne 4 -or
    @($models | Where-Object { $_.Role -cnotin $roles }).Count -ne 0 -or
    @($models.File | Sort-Object -Unique).Count -ne 4) {
    throw 'Expected one uniquely named artifact for each sample model role.'
}

foreach ($model in $models) {
    $uri = $null
    if ($model.File -notmatch '^[A-Za-z0-9_-][A-Za-z0-9._-]*\.onnx$' -or
        ($model.Bytes -isnot [long] -and $model.Bytes -isnot [int]) -or
        $model.Bytes -le 0 -or
        $model.Hash -notmatch '^[0-9A-Fa-f]{64}$' -or
        -not [Uri]::TryCreate($model.Uri, [UriKind]::Absolute, [ref] $uri) -or
        $uri.Scheme -ne 'https' -or $uri.UserInfo.Length -ne 0) {
        throw "Invalid pinned model configuration for role '$($model.Role)'."
    }
}

$directory = [System.IO.Path]::GetFullPath($ModelsDirectory)
if ($VerifyOnly) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Missing model directory: $directory. Verification-only mode does not create it."
    }
}
else {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

foreach ($model in $models) {
    Save-ModelFile -Directory $directory -Model $model -VerifyOnly:$VerifyOnly
}
