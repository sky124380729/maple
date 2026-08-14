$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$statusPath = Join-Path $dist 'hid-post-reboot-status.json'
$logPath = Join-Path $dist 'hid-post-reboot-setup.log'
$selfTestPath = Join-Path $dist 'hid-device-report.json'
$taskName = 'MapleHidPostRebootSetup'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

try {
    & (Join-Path $PSScriptRoot 'install-maple-vhf-driver.ps1') -TrustTestCertificate *>&1 |
        Tee-Object -FilePath $logPath
    if ($LASTEXITCODE -ne 0) { throw "HID_INSTALL_EXIT:$LASTEXITCODE" }

    $exe = Join-Path $root 'dist\windows-x64\Maple.exe'
    $process = Start-Process -FilePath $exe -ArgumentList @('--hid-device-self-test', ('"' + $selfTestPath + '"')) -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "HID_DEVICE_SELF_TEST_EXIT:$($process.ExitCode)" }

    [ordered]@{
        status = 'PASS'
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        deviceReport = $selfTestPath
        log = $logPath
    } | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
}
catch {
    [ordered]@{
        status = 'FAIL'
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        error = $_.Exception.Message
        log = $logPath
    } | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
    $_ | Out-String | Add-Content -LiteralPath $logPath -Encoding UTF8
}
finally {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
}
