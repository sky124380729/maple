param([switch]$RemoveTestCertificate)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cer = Join-Path $root 'driver\MapleVhfKeyboard\bin\x64\Release\MapleVhfKeyboard\MapleVhfKeyboard.cer'
$devcon = 'C:\Program Files (x86)\Windows Kits\10\Tools\10.0.28000.0\x64\devcon.exe'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'HID_UNINSTALL_REQUIRES_ADMINISTRATOR'
}
& $devcon remove 'Root\MapleVhfKeyboard' | Out-Host
if ($LASTEXITCODE -notin @(0, 1)) { throw "HID_DEVICE_REMOVE_FAILED:$LASTEXITCODE" }
if ($RemoveTestCertificate -and (Test-Path -LiteralPath $cer -PathType Leaf)) {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cer)
    & certutil.exe -delstore Root $certificate.Thumbprint | Out-Host
    & certutil.exe -delstore TrustedPublisher $certificate.Thumbprint | Out-Host
}
Write-Output 'HID_DRIVER_DEVICE_REMOVE=PASS'
