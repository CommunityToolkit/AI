param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$revision = '40bde044036bb181c130ddf6c51792187268748f'
$expectedHash = '59C81A3A2923042D85034FFC487F8F47E4854117E879AEF89B2B9F728FB4922A'
$url = "https://huggingface.co/docling-project/docling-layout-heron-onnx/resolve/$revision/model.onnx"
$resolved = [System.IO.Path]::GetFullPath($OutputPath)

if (Test-Path -LiteralPath $resolved) {
    if ((Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash -ne $expectedHash) {
        throw "Existing model does not match the pinned Heron SHA256: $resolved"
    }
    Write-Output $resolved
    return
}

$parent = Split-Path -Parent $resolved
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$temporary = "$resolved.$([Guid]::NewGuid().ToString('N')).download"
try {
    # Explicit user/CI acquisition only. The runtime package never invokes this script.
    Invoke-WebRequest -Uri $url -OutFile $temporary
    if ((Get-Item -LiteralPath $temporary).Length -ne 171220471 -or
        (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Downloaded model does not match the pinned Heron size/SHA256.'
    }
    Move-Item -LiteralPath $temporary -Destination $resolved
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary
    }
}

Write-Output $resolved
