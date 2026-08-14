[CmdletBinding()]
param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\MaplePrototype\MaplePrototype.csproj'
$msbuildCandidates = @(
  'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
  'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
)
$msbuild = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found. Install the Visual Studio .NET desktop workload.' }

& $msbuild $project /t:Build /p:Configuration=$Configuration /p:Platform=AnyCPU /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }

$source = Join-Path $root "src\MaplePrototype\bin\$Configuration\MapleVisualPrototype.exe"
$destination = Join-Path $root 'dist\MapleVisualPrototype.exe'
Copy-Item -LiteralPath $source -Destination $destination -Force
Write-Output "BUILT=$destination"
