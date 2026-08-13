Set-StrictMode -Version Latest
$script:Probe = Join-Path $PSScriptRoot '..\tools\input-probe.ps1'

function Assert-Equal([object]$Expected, [object]$Actual, [string]$Message) {
  if ($Expected -ne $Actual) { throw "${Message}: expected '$Expected', got '$Actual'" }
}

Assert-Equal 'rejected-not-armed' (& powershell -NoProfile -ExecutionPolicy Bypass -File $script:Probe -DecisionOnly) 'not armed'
Assert-Equal 'rejected-not-foreground' (& powershell -NoProfile -ExecutionPolicy Bypass -File $script:Probe -DecisionOnly -Armed) 'not foreground'
Assert-Equal 'allowed' (& powershell -NoProfile -ExecutionPolicy Bypass -File $script:Probe -DecisionOnly -Armed -Foreground) 'armed foreground'
Write-Output 'PASS input probe guard logic'
