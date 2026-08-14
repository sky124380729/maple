param(
    [string]$PublishDirectory = 'dist\windows-x64',
    [string]$EvidencePath = 'dist\windows-target-capture-evidence.json',
    [ValidateRange(10, 600)]
    [int]$FrameCount = 60,
    [switch]$RequireForeground
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publish = [IO.Path]::GetFullPath((Join-Path $root $PublishDirectory))
$evidence = [IO.Path]::GetFullPath((Join-Path $root $EvidencePath))
$executable = Join-Path $publish 'Maple.exe'

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish_contract.tests.ps1') -PublishDirectory $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish contract failed' }

if (Test-Path -LiteralPath $evidence -PathType Leaf) { Remove-Item -LiteralPath $evidence -Force }
$process = Start-Process -FilePath $executable -ArgumentList @('--target-capture-test', $evidence, $FrameCount) -Wait -PassThru
if (-not (Test-Path -LiteralPath $evidence -PathType Leaf)) { throw "Target capture evidence missing: $evidence" }
$report = Get-Content -LiteralPath $evidence -Raw -Encoding UTF8 | ConvertFrom-Json
if ($report.inputStatus -ne 'INPUT_INJECTION=DISABLED') { throw 'Target capture evidence enabled an input path' }

if ($RequireForeground) {
    if ($process.ExitCode -ne 0) { throw "Target capture failed with $($report.code)" }
    if (-not $report.success -or $report.code -ne 'CLIENT_CAPTURE_PASS') { throw "Target capture evidence is invalid: $($report.code)" }
    if ($report.capturedFrames -ne $FrameCount -or $report.effectiveFps -le 0) { throw 'Target capture frame metrics are invalid' }
    Write-Output "TARGET_CAPTURE_EVIDENCE=PASS;Frames=$($report.capturedFrames);FPS=$($report.effectiveFps);P50=$($report.p50CaptureDurationMs);P95=$($report.p95CaptureDurationMs);Backends=$($report.captureBackends | ConvertTo-Json -Compress)"
    exit 0
}

Write-Output "TARGET_CAPTURE_EVIDENCE=$($report.code);ExitCode=$($process.ExitCode);Frames=$($report.capturedFrames);Input=$($report.inputStatus)"
