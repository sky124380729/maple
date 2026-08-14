param([switch]$SkipE2E)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ui = Join-Path $root 'ui'
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js is required' }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'npm is required' }

Push-Location $ui
try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    foreach ($command in @('lint', 'test', 'build')) {
        & npm run $command
        if ($LASTEXITCODE -ne 0) { throw "npm run $command failed" }
    }
    if (-not $SkipE2E) {
        & npm run e2e
        if ($LASTEXITCODE -ne 0) { throw 'npm run e2e failed' }
    }
}
finally {
    Pop-Location
}
Write-Output 'REACT_UI_BUILD=PASS'
