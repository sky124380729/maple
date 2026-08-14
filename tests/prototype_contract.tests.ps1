$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\MaplePrototype\MaplePrototype.csproj'
$program = Join-Path $root 'src\MaplePrototype\Program.cs'
$selfTest = Join-Path $root 'dist\MapleVisualPrototype.exe'
$selfTestReport = Join-Path $root 'dist\prototype-self-test.txt'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Prototype project is missing: $project"
}
if (-not (Test-Path -LiteralPath $program)) {
    throw "Prototype source is missing: $program"
}

$source = Get-Content -LiteralPath $program -Raw -Encoding UTF8
foreach ($required in @('MapScanning', 'MapCalibrating', 'Observing', 'EmergencyStop', 'Left/Right/Up/Down', 'Alt', 'Z', 'SAFE_OBSERVE_ONLY')) {
    if ($source.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Prototype source is missing required contract text: $required"
    }
}

foreach ($forbidden in @('SendInput', 'keybd_event', 'PostMessage', 'mouse_event')) {
    if ($source.IndexOf($forbidden, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Prototype must not contain input injection API: $forbidden"
    }
}

if (-not (Test-Path -LiteralPath $selfTest)) {
    throw "Built prototype is missing: $selfTest"
}

$null = Remove-Item -LiteralPath $selfTestReport -Force -ErrorAction SilentlyContinue
$output = & $selfTest --self-test 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Prototype self-test failed with exit code $LASTEXITCODE`n$output"
}
$output = if (Test-Path -LiteralPath $selfTestReport) { Get-Content -LiteralPath $selfTestReport -Raw -Encoding UTF8 } else { $output }
foreach ($required in @('PROTOTYPE_MODE=SAFE_OBSERVE_ONLY', 'INPUT_INJECTION=DISABLED', 'TARGET_TITLE=', 'MOVEMENT_KEYS=Left/Right/Up/Down', 'JUMP_KEY=Alt', 'PICKUP=OPTIONAL_DEFAULT_Z')) {
    if ($output.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Prototype self-test is missing: $required`n$output"
    }
}

Write-Output 'PROTOTYPE_CONTRACT=PASS'
