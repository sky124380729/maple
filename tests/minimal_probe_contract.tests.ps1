Set-StrictMode -Version Latest

$sourcePath = Join-Path $PSScriptRoot '..\src\MapleInputProbe\Program.cs'
$source = Get-Content -Raw -LiteralPath $sourcePath

function Assert-Contains([string]$Text, [string]$Needle, [string]$Message) {
  if ($Text.IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) {
    throw "${Message}: missing '$Needle'"
  }
}

Assert-Contains $source '--self-test' 'self-test command'
Assert-Contains $source '--authorized-auto-test' 'authorized automatic test command'
Assert-Contains $source 'CaptureTarget("auto-before")' 'automatic test before screenshot'
Assert-Contains $source 'CaptureTarget("auto-after-left")' 'automatic test left screenshot'
Assert-Contains $source 'CaptureTarget("auto-after")' 'automatic test after screenshot'
Assert-Contains $source 'CaptureTarget("auto-after-right")' 'automatic test right screenshot'
Assert-Contains $source 'NOT_INSTALLED' 'virtual HID readiness result'
Assert-Contains $source 'GLOBAL_HOTKEYS=DISABLED' 'physical hotkey policy'
Assert-Contains $source 'KEYEVENTF_KEYUP' 'explicit key release'
Assert-Contains $source 'if (sentUp == 1) activeScans.Remove(scan);' 'retain key when release fails'
Assert-Contains $source 'activateResult=' 'foreground activation diagnostic'
Assert-Contains $source 'foregroundConfirmed=' 'foreground confirmation diagnostic'
Assert-Contains $source 'Activate()' 'probe foreground handshake'
Assert-Contains $source 'ShowWindowAsync' 'target restore API'
Assert-Contains $source 'IsIconic' 'minimized-window guard'
$manifestPath = Join-Path $PSScriptRoot '..\src\MapleInputProbe\app.manifest'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'administrator manifest: file missing' }
$manifest = Get-Content -Raw -LiteralPath $manifestPath
Assert-Contains $manifest 'requestedExecutionLevel level="requireAdministrator"' 'administrator manifest'
Write-Output 'PASS minimal probe contract'
