$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$specPath = Join-Path $root 'docs\MAPLE_PROJECT_SPEC.md'
$handoffPath = Join-Path $root 'docs\WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md'
$portableContractsPath = Join-Path $root 'tests\portable-contracts.mjs'
$hostManifestPath = Join-Path $root 'src\Maple.Host\app.manifest'
$brokerManifestPath = Join-Path $root 'src\Maple.InputBroker\app.manifest'

function Assert-Contains([string]$Text, [string]$Expected, [string]$Description) {
    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$Description is missing required text: $Expected"
    }
}

function Assert-NotContains([string]$Text, [string]$Forbidden, [string]$Description) {
    if ($Text.IndexOf($Forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "$Description contains obsolete text: $Forbidden"
    }
}

function Assert-NotMatches([string]$Text, [string]$ForbiddenPattern, [string]$Description) {
    if ([regex]::IsMatch($Text, $ForbiddenPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw "$Description contains an obsolete production-input claim: $ForbiddenPattern"
    }
}

foreach ($path in @($specPath, $handoffPath, $portableContractsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Production input contract source is missing: $path"
    }
}

$spec = Get-Content -LiteralPath $specPath -Raw -Encoding UTF8
$handoff = Get-Content -LiteralPath $handoffPath -Raw -Encoding UTF8
$portableContracts = Get-Content -LiteralPath $portableContractsPath -Raw -Encoding UTF8
$combinedDocs = $spec + "`n" + $handoff

foreach ($required in @(
    'Maple.InputBroker.exe',
    'Maple.exe',
    'NORMAL_INTEGRITY',
    'ELEVATED',
    'EXTENDED_SCANCODE',
    'BrokerProtocol',
    'BrokerClient',
    'SOURCE_READY',
    'F9',
    'F12',
    'REARM_DELAY_MS=3000'
)) {
    Assert-Contains $spec $required 'Maple project specification production input contract'
    Assert-Contains $handoff $required 'Windows handoff production input contract'
}

foreach ($required in @('`vk`', '`scanCode`', '`flags`')) {
    Assert-Contains $spec $required 'Maple project specification raw input rejection'
}
foreach ($required in @(
    'React',
    'Host',
    'RAW_INPUT_FORBIDDEN',
    'TARGET_MISMATCH',
    'FOREGROUND_LOST',
    'STALE_FRAME',
    'IPC_FAILURE',
    'HEARTBEAT_TIMEOUT',
    'SHUTDOWN',
    'EXCEPTION',
    'RELEASE_ALL'
)) {
    Assert-Contains $spec $required 'Maple project specification fail-closed input contract'
}

foreach ($obsoletePattern in @(
    '\u751f\u4ea7\u8f93\u5165\u552f\u4e00\u901a\u8fc7\u72ec\u7acb\u865a\u62df HID',
    '\u751f\u4ea7\u8f93\u5165\u4ec5\u80fd\u7ecf\u8fc7\u5df2\u9a8c\u6536\u865a\u62df HID',
    '\u751f\u4ea7\u8f93\u5165\u53ea\u80fd\u6765\u81ea\u5df2\u9a8c\u6536\u865a\u62df HID',
    '\u552f\u4e00\u7684\u865a\u62df HID \u8f93\u5165'
)) {
    Assert-NotMatches $combinedDocs $obsoletePattern 'Active production input documentation'
}

foreach ($required in @('BrokerProtocol', 'BrokerClient', 'ReleaseAll', 'BrokerInputSession')) {
    Assert-Contains $portableContracts $required 'Portable broker source contract'
}
$trackedProductionPaths = @(& git -C $root ls-files -- 'src/**' 'tools/**' 'Maple.sln' 'README.md')
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked production files' }
$productionText = ($trackedProductionPaths | ForEach-Object {
    $path = Join-Path $root $_
    if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Content -LiteralPath $path -Raw -Encoding UTF8 }
}) -join "`n"
foreach ($forbidden in @('WindowsVirtualHidAdapter', 'MapleVhf', 'TESTSIGNING', 'enable-maple-driver-test-mode', 'hid_contract.tests.ps1')) {
    Assert-NotContains $productionText $forbidden 'Production source and publish boundary'
}

$hostManifest = Get-Content -LiteralPath $hostManifestPath -Raw -Encoding UTF8
$brokerManifest = Get-Content -LiteralPath $brokerManifestPath -Raw -Encoding UTF8
Assert-Contains $hostManifest 'level="asInvoker"' 'Host manifest'
Assert-NotContains $hostManifest 'requireAdministrator' 'Host manifest'
Assert-Contains $brokerManifest 'level="requireAdministrator"' 'Input Broker manifest'

$brokerSources = (Get-ChildItem -LiteralPath (Join-Path $root 'src\Maple.InputBroker') -Filter '*.cs' -File -Recurse | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
}) -join "`n"
$keybdEventDeclarations = [regex]::Matches($brokerSources, 'EntryPoint\s*=\s*"keybd_event"').Count
if ($keybdEventDeclarations -ne 1) {
    throw "Expected exactly one production keybd_event P/Invoke, found $keybdEventDeclarations"
}

$publishScript = Get-Content -LiteralPath (Join-Path $root 'tools\publish-windows.ps1') -Raw -Encoding UTF8
$publishContract = Get-Content -LiteralPath (Join-Path $root 'tests\windows\publish_contract.tests.ps1') -Raw -Encoding UTF8
Assert-Contains $publishScript 'Maple.InputBroker.exe' 'Windows publish script'
Assert-Contains $publishContract 'Maple.InputBroker.exe' 'Windows publish contract'

Write-Output 'PRODUCTION_INPUT_CONTRACT=PASS; BROKER_SOURCE=READY; WINDOWS_EVIDENCE=PENDING'
