param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'driver\MapleVhfKeyboard\MapleVhfKeyboard.vcxproj'
$output = Join-Path $root "driver\MapleVhfKeyboard\bin\$Platform\$Configuration"
$package = Join-Path $output 'MapleVhfKeyboard'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'

foreach ($required in @($project, $msbuild)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "HID driver build dependency missing: $required" }
}

& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:Platform=$Platform /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "HID driver build failed with exit code $LASTEXITCODE" }

$sys = Join-Path $package 'MapleVhfKeyboard.sys'
$inf = Join-Path $package 'MapleVhfKeyboard.inf'
$cat = Join-Path $package 'MapleVhfKeyboard.cat'
$certificateSource = Join-Path $output 'MapleVhfKeyboard.cer'
foreach ($required in @($sys, $inf, $cat)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "HID driver output missing: $required" }
}

$infText = Get-Content -LiteralPath $inf -Raw
foreach ($required in @('Root\MapleVhfKeyboard', 'LowerFilters', 'vhf', '{6E6E6F4A-21A5-4DD2-86E5-7DB4C7E8A101}')) {
    if ($infText.IndexOf($required, [StringComparison]::OrdinalIgnoreCase) -lt 0) { throw "HID INF contract missing: $required" }
}

$sysSignature = Get-AuthenticodeSignature -LiteralPath $sys
$catSignature = Get-AuthenticodeSignature -LiteralPath $cat
foreach ($signature in @($sysSignature, $catSignature)) {
    if ($null -eq $signature.SignerCertificate) { throw "HID package artifact is not signed: $($signature.Path)" }
}
if ($sysSignature.SignerCertificate.Thumbprint -ne $catSignature.SignerCertificate.Thumbprint) {
    throw 'HID SYS and catalog signatures do not use the same certificate.'
}
if (-not (Test-Path -LiteralPath $certificateSource -PathType Leaf)) { throw "HID test certificate missing: $certificateSource" }
$certificatePath = Join-Path $package 'MapleVhfKeyboard.cer'
Copy-Item -LiteralPath $certificateSource -Destination $certificatePath -Force
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
if ($certificate.Thumbprint -ne $sysSignature.SignerCertificate.Thumbprint) {
    throw 'HID package certificate does not match the SYS signature.'
}

$descriptorSource = Join-Path $root 'driver\MapleVhfKeyboard\keyboard-report-descriptor.hex'
if (-not (Test-Path -LiteralPath $descriptorSource -PathType Leaf)) { throw "HID report descriptor source missing: $descriptorSource" }
$descriptorBytes = @(
    (Get-Content -LiteralPath $descriptorSource -Raw) -split '\s+' |
        Where-Object { $_ } |
        ForEach-Object { [Convert]::ToByte($_, 16) }
)
if ($descriptorBytes.Count -ne 45) { throw "HID report descriptor length must be 45 bytes, got $($descriptorBytes.Count)" }
$descriptorPath = Join-Path $package 'keyboard-report-descriptor.bin'
[IO.File]::WriteAllBytes($descriptorPath, [byte[]]$descriptorBytes)
$descriptorHash = (Get-FileHash -LiteralPath $descriptorPath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Output "HID_DRIVER_BUILD=PASS;DescriptorSha256=$descriptorHash;Signer=$($sysSignature.SignerCertificate.Thumbprint);Output=$package"
