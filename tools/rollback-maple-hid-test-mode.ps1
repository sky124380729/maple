$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$statusPath = Join-Path $root 'dist\hid-rollback-status.json'
$cer = Join-Path $root 'driver\MapleVhfKeyboard\bin\x64\Release\MapleVhfKeyboard\MapleVhfKeyboard.cer'
$devcon = 'C:\Program Files (x86)\Windows Kits\10\Tools\10.0.28000.0\x64\devcon.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'HID_ROLLBACK_REQUIRES_ADMINISTRATOR'
}

& $devcon remove 'Root\MapleVhfKeyboard' | Out-Host
& pnputil.exe /delete-driver oem68.inf /uninstall /force | Out-Host

if (Test-Path -LiteralPath $cer -PathType Leaf) {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cer)
    & certutil.exe -delstore Root $certificate.Thumbprint | Out-Host
    & certutil.exe -delstore TrustedPublisher $certificate.Thumbprint | Out-Host
}

& bcdedit.exe /set testsigning off | Out-Host
if ($LASTEXITCODE -ne 0) { throw "TEST_SIGNING_DISABLE_FAILED:$LASTEXITCODE" }
$boot = (& bcdedit.exe /enum 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $boot -match '(?im)^testsigning\s+Yes\s*$') {
    throw 'TEST_SIGNING_DISABLE_VERIFICATION_FAILED'
}

[ordered]@{
    status = 'READY_FOR_REBOOT'
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    removedDevice = 'Root\MapleVhfKeyboard'
    removedDriverPackage = 'oem68.inf'
    testSigning = 'DISABLED_REBOOT_REQUIRED'
} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
Write-Output "HID_ROLLBACK=PASS;Status=$statusPath"
