$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$scriptPath = Join-Path $PSScriptRoot 'Get-Models.ps1'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -ne 0) {
    throw ($parseErrors | Out-String)
}

foreach ($name in @('Assert-ModelFile', 'Save-ModelFile')) {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) {
        throw "Missing production function: $name"
    }

    . ([ScriptBlock]::Create($definition.Extent.Text))
}

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Throws {
    param([scriptblock] $Action, [string] $Pattern)
    $failure = $null
    try {
        & $Action | Out-Null
    }
    catch {
        $failure = $_
    }

    Assert-True ($null -ne $failure) 'Expected a failure.'
    Assert-True ($failure.Exception.Message -match $Pattern) "Unexpected failure: $failure"
}

function Invoke-WebRequest {
    param([Uri] $Uri, [string] $OutFile)
    Assert-True ($Uri.Host -eq 'fixture.invalid') 'Only the offline fixture URI is allowed.'
    $script:downloadCalls++
    & $script:downloadAction $OutFile
}

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "provisioning-test-$([Guid]::NewGuid().ToString('N'))"))
[System.IO.Directory]::CreateDirectory($root) | Out-Null
$destination = Join-Path $root 'model.onnx'
$bytes = [System.Text.Encoding]::UTF8.GetBytes('offline model provisioning fixture')
$model = @{
    File = 'model.onnx'
    Uri = 'https://fixture.invalid/model'
    Bytes = $bytes.Length
    Hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
}
$script:downloadCalls = 0
$script:downloadAction = { throw 'Unexpected download attempt.' }
$passed = 0

try {
    [System.IO.File]::WriteAllBytes($destination, $bytes)
    $timestamp = [System.IO.File]::GetLastWriteTimeUtc($destination)
    Save-ModelFile -Directory $root -Model $model | Out-Null
    Assert-True ($script:downloadCalls -eq 0) 'An existing valid model must not download.'
    Assert-True ([System.IO.File]::GetLastWriteTimeUtc($destination) -eq $timestamp) 'An existing valid model was rewritten.'
    $passed++

    Save-ModelFile -Directory $root -Model $model -VerifyOnly | Out-Null
    Assert-True ($script:downloadCalls -eq 0) 'Verification-only must not download.'
    $passed++

    $invalidBytes = [byte[]]::new($bytes.Length)
    [System.IO.File]::WriteAllBytes($destination, $invalidBytes)
    Assert-Throws { Save-ModelFile -Directory $root -Model $model } 'Unexpected model artifact'
    Assert-True ($script:downloadCalls -eq 0) 'An existing invalid model must not download.'
    Assert-True ([Convert]::ToHexString([System.IO.File]::ReadAllBytes($destination)) -eq [Convert]::ToHexString($invalidBytes)) 'An existing invalid model was overwritten.'
    Remove-Item -LiteralPath $destination
    $passed++

    Assert-Throws { Save-ModelFile -Directory $root -Model $model -VerifyOnly } 'Missing model artifact'
    Assert-True ($script:downloadCalls -eq 0) 'Verification-only downloaded a missing model.'
    $passed++

    $script:downloadAction = {
        param($path)
        Assert-True ([System.IO.Path]::GetFileName($path) -match '^\.model\.onnx\.[0-9a-f]{32}\.partial$') 'Download must target a unique temporary file.'
        Assert-True (-not (Test-Path -LiteralPath $destination)) 'A final file was exposed before verification.'
        [System.IO.File]::WriteAllBytes($path, $bytes)
        if ($IsWindows) {
            [System.IO.File]::SetAttributes($path, [System.IO.FileAttributes]::Hidden)
        }
    }
    Save-ModelFile -Directory $root -Model $model | Out-Null
    Assert-ModelFile -Path $destination -Model $model
    Assert-True (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 1) 'A successful download left temporary files.'
    Remove-Item -LiteralPath $destination -Force
    $passed++

    $script:downloadAction = {
        param($path)
        [System.IO.File]::WriteAllText($path, 'short')
    }
    Assert-Throws { Save-ModelFile -Directory $root -Model $model } 'Unexpected model artifact'
    Assert-True (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0) 'A size failure exposed a final file or left a temporary file.'
    $passed++

    $script:downloadAction = {
        param($path)
        [System.IO.File]::WriteAllBytes($path, $invalidBytes)
    }
    Assert-Throws { Save-ModelFile -Directory $root -Model $model } 'Unexpected model artifact'
    Assert-True (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0) 'A hash failure exposed a final file or left a temporary file.'
    $passed++

    $script:downloadAction = {
        param($path)
        [System.IO.File]::WriteAllText($path, 'partial download')
        throw 'Offline simulated network failure.'
    }
    Assert-Throws { Save-ModelFile -Directory $root -Model $model } 'Offline simulated network failure'
    Assert-True (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0) 'A download failure left an incomplete artifact.'
    $passed++

    $script:downloadAction = {
        param($path)
        [System.IO.File]::WriteAllBytes($path, $bytes)
        [System.IO.File]::WriteAllText($destination, 'concurrent writer')
    }
    Assert-Throws { Save-ModelFile -Directory $root -Model $model } 'already exists'
    Assert-True ([System.IO.File]::ReadAllText($destination) -eq 'concurrent writer') 'A concurrent destination was overwritten.'
    Assert-True (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 1) 'A concurrent destination failure left a temporary file.'
    $passed++

    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'models.json') -Raw | ConvertFrom-Json
    Assert-True (($manifest.Models | Measure-Object -Property Bytes -Sum).Sum -eq 308482462) 'Pinned model size inventory changed.'
    $expectedPins = @{
        Layout = '59C81A3A2923042D85034FFC487F8F47E4854117E879AEF89B2B9F728FB4922A'
        OcrDetector = '4D97C44A20D30A81AAD087D6A396B08F786C4635742AFC391F6621F5C6AE78AE'
        OcrRecognizer = '5825FC7EBF84AE7A412BE049820B4D86D77620F204A041697B0494669B1742C5'
        TableStructure = '2C90A63298DF61006A45267932F47B345A8B104CE53FD504EACF11AEE3C05A41'
    }
    foreach ($artifact in $manifest.Models) {
        Assert-True ($artifact.Hash -ceq $expectedPins[$artifact.Role]) "Pinned model identity changed: $($artifact.Role)"
    }
    Assert-Throws { & $scriptPath -ModelsDirectory $root -VerifyOnly } 'Missing model artifact'
    $passed++

    $absentDirectory = Join-Path $root 'absent'
    Assert-Throws { & $scriptPath -ModelsDirectory $absentDirectory -VerifyOnly } 'Missing model directory'
    Assert-True (-not (Test-Path -LiteralPath $absentDirectory)) 'Verification-only created a directory.'
    $passed++

    $fixtureScript = Join-Path $root 'Get-Models.ps1'
    [System.IO.File]::Copy($scriptPath, $fixtureScript)
    foreach ($mutation in @(
        { param($value) $value.Models[0].File = '../outside.onnx' },
        { param($value) $value.Models[0].Uri = 'http://fixture.invalid/model' },
        { param($value) $value.Models[0].Uri = 'https://user:password@fixture.invalid/model' },
        { param($value) $value.Models[0].Bytes = 0 },
        { param($value) $value.Models[0].Hash = 'not-a-hash' },
        { param($value) $value.Models[0].Bytes = '171220471' },
        { param($value) $value.Models[0].Role = 'Unknown' },
        { param($value) $value.Models[1].File = $value.Models[0].File },
        { param($value) $value.Models[1].Role = $value.Models[0].Role },
        { param($value) $value.SchemaVersion = 2 }
    )) {
        $invalidManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'models.json') -Raw | ConvertFrom-Json -AsHashtable
        & $mutation $invalidManifest
        [System.IO.File]::WriteAllText((Join-Path $root 'models.json'), ($invalidManifest | ConvertTo-Json -Depth 8))
        Assert-Throws { & $fixtureScript -ModelsDirectory $absentDirectory } 'Invalid pinned model configuration|Expected'
        Assert-True (-not (Test-Path -LiteralPath $absentDirectory)) 'Invalid configuration touched the destination directory.'
        $passed++
    }

    Assert-True ($script:downloadCalls -eq 5) 'Unexpected download attempts or retries.'
    Write-Output "$passed/21 offline provisioning checks passed. No network or model inference was performed."
}
finally {
    Get-ChildItem -LiteralPath $root -Force -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    Remove-Item -LiteralPath $root
}
