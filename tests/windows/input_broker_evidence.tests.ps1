param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot,
    [switch]$RequireEvidence
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Input Broker evidence directory missing: $root"
}

$jsonlFiles = @(Get-ChildItem -LiteralPath $root -Filter '*.jsonl' -File)
if ($jsonlFiles.Count -ne 1) {
    throw "Expected exactly one JSONL evidence file, found $($jsonlFiles.Count)"
}

$records = @()
$lineNumber = 0
foreach ($line in Get-Content -LiteralPath $jsonlFiles[0].FullName -Encoding UTF8) {
    $lineNumber++
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $records += $line | ConvertFrom-Json }
    catch { throw "Invalid evidence JSON at line ${lineNumber}: $($_.Exception.Message)" }
}

$requiredActions = @(
    'move-left',
    'move-right',
    'jump',
    'climb-up',
    'climb-down',
    'single-attack',
    'pickup',
    'release-all'
)
if ($records.Count -ne $requiredActions.Count) {
    throw "Expected $($requiredActions.Count) evidence records, found $($records.Count)"
}

function Require-Property($Record, [string]$Name) {
    $property = $Record.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Evidence record is missing required property: $Name" }
    return $property.Value
}

function Resolve-EvidenceFile([string]$RelativePath, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
        throw "$Description must be a relative evidence path"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    $prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description escapes the evidence directory"
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 8) {
        throw "$Description is missing or empty: $RelativePath"
    }
    if ([IO.Path]::GetExtension($path) -ne '.png') { throw "$Description must be a PNG file" }
    $signature = [IO.File]::ReadAllBytes($path)[0..7]
    $expected = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    for ($index = 0; $index -lt $expected.Length; $index++) {
        if ($signature[$index] -ne $expected[$index]) { throw "$Description is not a PNG file" }
    }
}

$seen = @{}
$previousObservedAt = $null
for ($recordIndex = 0; $recordIndex -lt $records.Count; $recordIndex++) {
    $record = $records[$recordIndex]
    $actionId = [string](Require-Property $record 'actionId')
    if ($actionId -notin $requiredActions) { throw "Unknown evidence action: $actionId" }
    if ($actionId -ne $requiredActions[$recordIndex]) { throw "Evidence action order is invalid at record $($recordIndex + 1)" }
    if ($seen.ContainsKey($actionId)) { throw "Duplicate evidence action: $actionId" }
    $seen[$actionId] = $true

    try { $observedAt = [DateTimeOffset]::Parse([string](Require-Property $record 'observedAtUtc'), [Globalization.CultureInfo]::InvariantCulture) }
    catch { throw "$actionId has an invalid observedAtUtc" }
    if ($observedAt.Offset -ne [TimeSpan]::Zero) { throw "$actionId observedAtUtc is not UTC" }
    if ($null -ne $previousObservedAt -and ($observedAt - $previousObservedAt).TotalSeconds -lt 3) {
        throw "$actionId was recorded less than three seconds after the previous action"
    }
    $previousObservedAt = $observedAt

    if ([long](Require-Property $record 'targetHwnd') -eq 0) { throw "$actionId has no target HWND" }
    if ([int](Require-Property $record 'targetPid') -le 0) { throw "$actionId has no target PID" }
    if ((Require-Property $record 'foregroundConfirmed') -ne $true) { throw "$actionId did not confirm foreground" }
    if ([int](Require-Property $record 'hostIntegrity') -ne 8192) { throw "$actionId Host integrity is not medium" }
    if ([int](Require-Property $record 'brokerIntegrity') -ne 12288) { throw "$actionId Broker integrity is not high" }
    if ([int](Require-Property $record 'targetIntegrity') -le 0) { throw "$actionId target integrity is unknown" }

    $vk = [int](Require-Property $record 'vk')
    $scanCode = [int](Require-Property $record 'scanCode')
    $flagsDown = [int](Require-Property $record 'flagsDown')
    $flagsUp = [int](Require-Property $record 'flagsUp')
    if ($actionId -eq 'release-all') {
        if ($vk -ne 0 -or $scanCode -ne 0 -or $flagsDown -ne 0 -or $flagsUp -ne 0) {
            throw 'release-all must not claim a key encoding'
        }
    }
    else {
        if ($vk -le 0 -or $scanCode -le 0) { throw "$actionId has an invalid key encoding" }
        if (($flagsDown -band 0x2) -ne 0 -or ($flagsUp -band 0x2) -eq 0) {
            throw "$actionId key-up flags are not paired"
        }
    }

    Resolve-EvidenceFile ([string](Require-Property $record 'screenshotBefore')) "$actionId before screenshot"
    Resolve-EvidenceFile ([string](Require-Property $record 'screenshotAfter')) "$actionId after screenshot"
    if ([string](Require-Property $record 'classification') -ne 'CLIENT_EFFECT_CONFIRMED') {
        throw "$actionId client effect is not confirmed"
    }
    if ((Require-Property $record 'allKeysReleased') -ne $true) { throw "$actionId left an active key" }
}

foreach ($required in $requiredActions) {
    if (-not $seen.ContainsKey($required)) { throw "Missing evidence action: $required" }
}

if ($RequireEvidence) {
    $liveEvidenceRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Maple\input-broker-evidence'))
    $liveEvidencePrefix = $liveEvidenceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $root.StartsWith($liveEvidencePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Real evidence must come from the Maple Input Broker evidence directory'
    }
}

Write-Output "INPUT_BROKER_EVIDENCE=PASS;ACTIONS=$($records.Count);ROOT=$root"
