param([switch]$RequireEvidence)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$adapterPath = Join-Path $root 'src\Maple.Input\WindowsVirtualHidAdapter.cs'
$diagnosticsPath = Join-Path $root 'src\Maple.Input\VirtualHidDiagnostics.cs'
foreach ($path in @($adapterPath, $diagnosticsPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "HID contract source missing: $path" }
}

$adapter = Get-Content -LiteralPath $adapterPath -Raw -Encoding UTF8
foreach ($required in @('VirtualHidDeviceContract', 'IVirtualHidTransport', 'IVirtualHidReportEncoder', 'ReleaseAll', 'Heartbeat', 'HID_CONTRACT_UNVERIFIED')) {
    if ($adapter.IndexOf($required, [StringComparison]::Ordinal) -lt 0) { throw "HID adapter is missing: $required" }
}

$evidenceFiles = @(
    'dist\hid-device-report.json',
    'dist\hid-os-report.json',
    'dist\hid-client-response.json'
)
if ($RequireEvidence) {
    foreach ($relative in $evidenceFiles) {
        $path = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $path)) { throw "Required Windows HID evidence missing: $relative" }
        $json = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($json.status -ne 'PASS') { throw "HID evidence has not passed: $relative" }
    }
    Write-Output 'HID_WINDOWS_EVIDENCE=PASS'
}
else {
    Write-Output 'HID_CONTRACT=PASS; HID_WINDOWS_EVIDENCE=NOT_REQUESTED'
}
