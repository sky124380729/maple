[CmdletBinding()]
param(
  [switch]$DecisionOnly,
  [switch]$Armed,
  [switch]$Foreground,
  [ValidateSet('A','D','W','S','J','K','L','SPACE')]
  [string]$Key = 'J',
  [int]$HoldMilliseconds = 80,
  [string]$WindowTitle = ''
)

Set-StrictMode -Version Latest

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeInputProbe {
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION U; }
  [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
  public const uint INPUT_KEYBOARD=1, KEYEVENTF_KEYUP=2;
  public static string ForegroundTitle() { var h=GetForegroundWindow(); var s=new System.Text.StringBuilder(512); GetWindowText(h,s,s.Capacity); return s.ToString(); }
}
'@

function Get-Decision([bool]$IsArmed, [bool]$IsForeground) {
  if (-not $IsArmed) { return 'rejected-not-armed' }
  if (-not $IsForeground) { return 'rejected-not-foreground' }
  return 'allowed'
}

$decision = Get-Decision $Armed.IsPresent $Foreground.IsPresent
if ($DecisionOnly) { Write-Output $decision; exit 0 }

if (-not $Armed) {
  Write-Output 'Not sent: pass -Armed:$true explicitly. Default is disarmed.'
  exit 2
}

$activeTitle = [NativeInputProbe]::ForegroundTitle()
if ([string]::IsNullOrWhiteSpace($WindowTitle)) {
  $WindowTitle = Read-Host 'Enter a unique fragment of the target game window title'
}
if ([string]::IsNullOrWhiteSpace($WindowTitle) -or $activeTitle -notlike "*$WindowTitle*") {
  Write-Output "Not sent: foreground window mismatch. Current foreground window: $activeTitle"
  exit 3
}

$vk = @{ A=0x41; D=0x44; W=0x57; S=0x53; J=0x4A; K=0x4B; L=0x4C; SPACE=0x20 }[$Key]
$down = [NativeInputProbe+INPUT]::new(); $down.type=[NativeInputProbe]::INPUT_KEYBOARD; $down.U.ki.wVk=[uint16]$vk
$up = [NativeInputProbe+INPUT]::new(); $up.type=[NativeInputProbe]::INPUT_KEYBOARD; $up.U.ki.wVk=[uint16]$vk; $up.U.ki.dwFlags=[NativeInputProbe]::KEYEVENTF_KEYUP
$size = [Runtime.InteropServices.Marshal]::SizeOf([NativeInputProbe+INPUT])
$sentDown = [NativeInputProbe]::SendInput(1, @($down), $size)
Start-Sleep -Milliseconds ([Math]::Max(20, [Math]::Min($HoldMilliseconds, 500)))
$sentUp = [NativeInputProbe]::SendInput(1, @($up), $size)
Write-Output "Sent one key: $Key; down=$sentDown, up=$sentUp; foreground=$activeTitle"
