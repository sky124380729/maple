param([switch]$IUnderstandThisChangesWindowsBootPolicy)

$ErrorActionPreference = 'Stop'
if (-not $IUnderstandThisChangesWindowsBootPolicy) {
    throw 'EXPLICIT_TEST_SIGNING_APPROVAL_REQUIRED'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'TEST_SIGNING_CHANGE_REQUIRES_ADMINISTRATOR'
}
$secureBoot = Get-ItemPropertyValue -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -Name UEFISecureBootEnabled -ErrorAction Stop
if ($secureBoot -ne 0) { throw 'TEST_SIGNING_BLOCKED_BY_SECURE_BOOT' }
& bcdedit.exe /set testsigning on | Out-Host
if ($LASTEXITCODE -ne 0) { throw "TEST_SIGNING_ENABLE_FAILED:$LASTEXITCODE" }
Write-Output 'TEST_SIGNING_ENABLED_REBOOT_REQUIRED'
