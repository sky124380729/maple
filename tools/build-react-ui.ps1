param([switch]$SkipE2E)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ui = Join-Path $root 'ui'
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js is required' }
$npmCommand = if ($env:OS -eq 'Windows_NT') {
    (Get-Command npm.cmd -ErrorAction Stop).Source
}
else {
    (Get-Command npm -ErrorAction Stop).Source
}

Push-Location $ui
try {
    & $npmCommand ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    foreach ($command in @('lint', 'test', 'build')) {
        & $npmCommand run $command
        if ($LASTEXITCODE -ne 0) { throw "npm run $command failed" }
    }
    if (-not $SkipE2E) {
        & $npmCommand run e2e
        if ($LASTEXITCODE -ne 0) { throw 'npm run e2e failed' }
    }
}
finally {
    Pop-Location
}
Write-Output 'REACT_UI_BUILD=PASS'
