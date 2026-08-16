param(
    [string]$OutputDirectory = 'dist\windows-x64',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipE2E,
    [string]$InspectModelPath,
    [string]$InspectionOutput = 'artifacts\model-inspection.json'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$localDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$publishedExecutable = Join-Path $publish 'Maple.exe'
$publishedBroker = Join-Path $publish 'Maple.InputBroker.exe'
$lockingProcesses = @(Get-Process -Name Maple, Maple.InputBroker -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -in @($publishedExecutable, $publishedBroker) } catch { $false }
})
if ($lockingProcesses.Count -gt 0) {
    $processIds = ($lockingProcesses | Select-Object -ExpandProperty Id) -join ', '
    throw "Published Maple or Maple.InputBroker is still running from the output directory (PID: $processIds). Close it before publishing."
}

$uiBuildScript = Join-Path $PSScriptRoot 'build-react-ui.ps1'
if ($SkipE2E) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $uiBuildScript -SkipE2E
}
else {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $uiBuildScript
}
if ($LASTEXITCODE -ne 0) { throw 'React UI build failed' }

& $dotnet publish (Join-Path $root 'src\Maple.Host\Maple.Host.csproj') `
    -c $Configuration `
    -r win-x64 `
    --no-restore `
    --self-contained true `
    --nologo `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows host publish failed' }
if (-not (Test-Path -LiteralPath $publishedBroker -PathType Leaf)) {
    throw "Published Input Broker is missing: $publishedBroker"
}

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\windows\publish_contract.tests.ps1') -PublishDirectory $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish contract failed' }

if ($InspectModelPath) {
    $inspection = [IO.Path]::GetFullPath((Join-Path $root $InspectionOutput))
    & $publishedExecutable --inspect-model ([IO.Path]::GetFullPath($InspectModelPath)) --output $inspection
    if ($LASTEXITCODE -ne 0) { throw "ONNX model inspection failed; report: $inspection" }
    Write-Output "MODEL_INSPECTION=PASS;$inspection"
}

Write-Output "WINDOWS_PUBLISH=PASS;$publish"
