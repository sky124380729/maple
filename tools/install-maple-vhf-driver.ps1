param([switch]$TrustTestCertificate)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$package = Join-Path $root 'driver\MapleVhfKeyboard\bin\x64\Release\MapleVhfKeyboard'
$inf = Join-Path $package 'MapleVhfKeyboard.inf'
$sys = Join-Path $package 'MapleVhfKeyboard.sys'
$cat = Join-Path $package 'MapleVhfKeyboard.cat'
$cer = Join-Path $package 'MapleVhfKeyboard.cer'
$devcon = 'C:\Program Files (x86)\Windows Kits\10\Tools\10.0.28000.0\x64\devcon.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'HID_INSTALL_REQUIRES_ADMINISTRATOR'
}
foreach ($required in @($inf, $sys, $cat, $cer, $devcon)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "HID_INSTALL_FILE_MISSING:$required" }
}

$sysSignature = Get-AuthenticodeSignature -LiteralPath $sys
$catSignature = Get-AuthenticodeSignature -LiteralPath $cat
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cer)
if ($null -eq $sysSignature.SignerCertificate -or $null -eq $catSignature.SignerCertificate) {
    throw 'HID_INSTALL_PACKAGE_UNSIGNED'
}
if ($sysSignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or
    $catSignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw 'HID_INSTALL_SIGNATURE_MISMATCH'
}

$boot = (& bcdedit /enum 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'HID_INSTALL_BOOT_POLICY_UNREADABLE' }
if ($boot -notmatch '(?im)^testsigning\s+Yes\s*$') {
    throw 'HID_TEST_SIGNING_REQUIRED: run enable-maple-driver-test-mode.ps1 after explicit approval, reboot, then retry'
}

function Has-Certificate([string]$storePath, [string]$thumbprint) {
    return $null -ne (Get-ChildItem -LiteralPath $storePath | Where-Object Thumbprint -eq $thumbprint | Select-Object -First 1)
}

if ($TrustTestCertificate) {
    & certutil.exe -f -addstore Root $cer | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'HID_ROOT_CERTIFICATE_INSTALL_FAILED' }
    & certutil.exe -f -addstore TrustedPublisher $cer | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'HID_PUBLISHER_CERTIFICATE_INSTALL_FAILED' }
}
if (-not (Has-Certificate Cert:\LocalMachine\Root $certificate.Thumbprint) -or
    -not (Has-Certificate Cert:\LocalMachine\TrustedPublisher $certificate.Thumbprint)) {
    throw 'HID_TEST_CERTIFICATE_NOT_TRUSTED: rerun with -TrustTestCertificate'
}

& $devcon install $inf 'Root\MapleVhfKeyboard' | Out-Host
if ($LASTEXITCODE -ne 0) { throw "HID_DEVICE_INSTALL_FAILED:$LASTEXITCODE" }
& pnputil.exe /enum-interfaces /class '{6E6E6F4A-21A5-4DD2-86E5-7DB4C7E8A101}' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'HID_DEVICE_INTERFACE_ENUM_FAILED' }
Write-Output "HID_DRIVER_INSTALL=PASS;CertificateThumbprint=$($certificate.Thumbprint)"
