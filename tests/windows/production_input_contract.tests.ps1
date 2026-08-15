$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$specPath = Join-Path $root 'docs\MAPLE_PROJECT_SPEC.md'
$handoffPath = Join-Path $root 'docs\WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md'
$portableContractsPath = Join-Path $root 'tests\portable-contracts.mjs'

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
    'PENDING_SOURCE',
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

foreach ($required in @('BrokerProtocol', 'BrokerClient', 'ReleaseAll', 'PENDING_SOURCE')) {
    Assert-Contains $portableContracts $required 'Portable broker contract migration'
}
Assert-NotContains $portableContracts "requireTokens('src/Maple.Input/WindowsVirtualHidAdapter.cs'" 'Portable production input source of truth'

Write-Output 'PRODUCTION_INPUT_CONTRACT=PASS; BROKER_SOURCE=PENDING'
