param(
    [string]$PublishDirectory = 'dist\windows-x64',
    [string]$EvidencePath = 'dist\windows-runtime-diagnostic.json',
    [string]$WgcEvidencePath = 'dist\windows-wgc-self-test.json'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publish = [IO.Path]::GetFullPath((Join-Path $root $PublishDirectory))
$evidence = [IO.Path]::GetFullPath((Join-Path $root $EvidencePath))
$wgcEvidence = [IO.Path]::GetFullPath((Join-Path $root $WgcEvidencePath))
$executable = Join-Path $publish 'Maple.exe'
$localDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish_contract.tests.ps1') -PublishDirectory $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish contract failed' }

$diagnostics = Start-Process -FilePath $executable -ArgumentList @('--windows-diagnostics', $evidence) -Wait -PassThru
if ($diagnostics.ExitCode -ne 0) { throw "Windows diagnostics failed with exit code $($diagnostics.ExitCode)" }
if (-not (Test-Path -LiteralPath $evidence -PathType Leaf)) { throw "Windows diagnostics evidence missing: $evidence" }
$report = Get-Content -LiteralPath $evidence -Raw -Encoding UTF8 | ConvertFrom-Json

if ($report.webView2.code -ne 'WEBVIEW2_READY') { throw "WebView2 is not ready: $($report.webView2.code)" }
if ($report.inputAdapter -ne 'NullInputAdapter' -or $report.inputStatus -ne 'INPUT_INJECTION=DISABLED') {
    throw 'Windows smoke must keep production input disabled'
}
if ($report.wgcStatus -ne 'WINDOWS_PENDING') { throw 'WGC must remain pending until real frame evidence exists' }
if ($report.modelStatus -ne 'MODEL_PENDING') { throw 'Models must remain pending until real model evidence exists' }
if ($report.hidStatus -ne 'HID_CONTRACT_UNVERIFIED') { throw 'HID must remain unverified without three-layer evidence' }

$wgcProcess = Start-Process -FilePath $executable -ArgumentList @('--wgc-self-test', $wgcEvidence) -Wait -PassThru
if ($wgcProcess.ExitCode -ne 0) { throw "WGC self-test failed with exit code $($wgcProcess.ExitCode)" }
$wgcReport = Get-Content -LiteralPath $wgcEvidence -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $wgcReport.success -or $wgcReport.code -ne 'WGC_SELF_TEST_PASS' -or $wgcReport.backend -ne 'Wgc' -or -not $wgcReport.nonBlack) {
    throw "WGC self-test evidence is invalid: $($wgcReport.code)"
}
if ($wgcReport.width -ne 640 -or $wgcReport.height -ne 360) { throw 'WGC self-test client crop is invalid' }

$appProcess = $null
$closeRequested = $false
try {
    $appProcess = Start-Process -FilePath $executable -PassThru
    $ready = $false
    for ($index = 0; $index -lt 40; $index++) {
        Start-Sleep -Milliseconds 250
        $appProcess.Refresh()
        if ($appProcess.HasExited) { break }
        if ($appProcess.MainWindowHandle -ne 0 -and $appProcess.Responding -and -not [string]::IsNullOrWhiteSpace($appProcess.MainWindowTitle)) {
            $ready = $true
            break
        }
    }
    if (-not $ready) { throw 'Published Maple host did not become ready' }
    $closeRequested = $appProcess.CloseMainWindow()
    if (-not $appProcess.WaitForExit(5000)) { throw 'Published Maple host did not close normally' }
}
finally {
    if ($null -ne $appProcess) {
        try {
            $appProcess.Refresh()
            if (-not $appProcess.HasExited) {
                [void]$appProcess.CloseMainWindow()
                if (-not $appProcess.WaitForExit(2000)) {
                    Stop-Process -Id $appProcess.Id
                    $appProcess.WaitForExit()
                }
            }
        }
        catch {
            Write-Warning "Failed to clean up Maple smoke process: $($_.Exception.Message)"
        }
    }
}

& $dotnet test (Join-Path $root 'src\Maple.Runtime.Tests\Maple.Runtime.Tests.csproj') `
    --filter WindowsDpapiCredentialStoreTests `
    --no-restore `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'Windows DPAPI credential test failed' }

Write-Output "WINDOWS_RUNTIME_SMOKE=PASS;OS=$($report.osDescription);WebView2=$($report.webView2.installedVersion);WGC=$($wgcReport.code);WgcReadbackMs=$($wgcReport.captureDurationMs);Target=$($report.target.diagnosticCode);DPI=$($report.target.candidates[0].dpi);Input=$($report.inputStatus);CloseRequested=$closeRequested"
