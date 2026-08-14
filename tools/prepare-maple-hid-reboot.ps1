$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'HID_REBOOT_PREPARE_REQUIRES_ADMINISTRATOR'
}

$root = Split-Path -Parent $PSScriptRoot
$postReboot = Join-Path $PSScriptRoot 'post-reboot-maple-hid-setup.ps1'
& (Join-Path $PSScriptRoot 'enable-maple-driver-test-mode.ps1') -IUnderstandThisChangesWindowsBootPolicy
if ($LASTEXITCODE -ne 0) { throw "TEST_SIGNING_ENABLE_EXIT:$LASTEXITCODE" }

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -File "' + $postReboot + '"')
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -StartWhenAvailable
Register-ScheduledTask -TaskName 'MapleHidPostRebootSetup' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
$registeredTask = Get-ScheduledTask -TaskName 'MapleHidPostRebootSetup' -ErrorAction Stop
if ($registeredTask.Actions.Execute -ne 'powershell.exe' -or $registeredTask.Actions.Arguments -notlike '*post-reboot-maple-hid-setup.ps1*') {
    throw 'HID_POST_REBOOT_TASK_VERIFICATION_FAILED'
}
$boot = (& bcdedit.exe /enum 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $boot -notmatch '(?im)^testsigning\s+Yes\s*$') {
    throw 'TEST_SIGNING_VERIFICATION_FAILED'
}

$statusPath = Join-Path $root 'dist\hid-reboot-prepared.json'
[ordered]@{
    status = 'READY'
    preparedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    scheduledTask = 'MapleHidPostRebootSetup'
    taskState = [string]$registeredTask.State
    taskExecute = $registeredTask.Actions.Execute
    taskArguments = $registeredTask.Actions.Arguments
    testSigning = 'ENABLED_REBOOT_REQUIRED'
} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
Write-Output "HID_REBOOT_PREPARED=PASS;Status=$statusPath"
