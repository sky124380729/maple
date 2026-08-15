param([switch]$SourceOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectPath = Join-Path $root 'src\Maple.InputProbe\Maple.InputProbe.csproj'
$manifestPath = Join-Path $root 'src\Maple.InputProbe\app.manifest'
$probeDirectory = Join-Path $root 'src\Maple.InputProbe'
$hostProjectPath = Join-Path $root 'src\Maple.Host\Maple.Host.csproj'
$specPath = Join-Path $root 'docs\MAPLE_PROJECT_SPEC.md'
$handoffPath = Join-Path $root 'docs\WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md'
$solutionPath = Join-Path $root 'Maple.sln'

function Assert-FileExists([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
}

function Assert-Equal([string]$Actual, [string]$Expected, [string]$Description) {
    if ($Actual -cne $Expected) {
        throw "$Description must be '$Expected', but was '$Actual'."
    }
}

function Assert-Contains([string]$Text, [string]$Expected, [string]$Description) {
    if ($Text.IndexOf($Expected, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "$Description is missing required text: $Expected"
    }
}

function Get-TextBetween([string]$Text, [string]$StartMarker, [string]$EndMarker, [string]$Description) {
    $startIndex = $Text.IndexOf($StartMarker, [StringComparison]::Ordinal)
    if ($startIndex -lt 0) {
        throw "$Description start marker is missing: $StartMarker"
    }

    $endIndex = $Text.IndexOf($EndMarker, $startIndex, [StringComparison]::Ordinal)
    if ($endIndex -lt 0) {
        throw "$Description end marker is missing: $EndMarker"
    }

    return $Text.Substring($startIndex, $endIndex - $startIndex)
}

Assert-FileExists $projectPath 'Input probe project'
Assert-FileExists $manifestPath 'Input probe application manifest'
Assert-FileExists $hostProjectPath 'Maple.Host project'
Assert-FileExists $specPath 'Maple project specification'
Assert-FileExists $handoffPath 'Windows implementation handoff'
Assert-FileExists $solutionPath 'Maple solution'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$properties = $project.Project.PropertyGroup | Select-Object -First 1
Assert-Equal $project.Project.Sdk 'Microsoft.NET.Sdk' 'Input probe SDK'
Assert-Equal $properties.OutputType 'WinExe' 'Input probe output type'
Assert-Equal $properties.TargetFramework 'net8.0-windows10.0.19041.0' 'Input probe target framework'
Assert-Equal $properties.UseWindowsForms 'true' 'Input probe WinForms setting'
Assert-Equal $properties.RuntimeIdentifier 'win-x64' 'Input probe runtime identifier'
Assert-Equal $properties.PlatformTarget 'x64' 'Input probe platform target'
Assert-Equal $properties.SelfContained 'true' 'Input probe self-contained setting'
Assert-Equal $properties.Nullable 'disable' 'Input probe nullable setting'
Assert-Equal $properties.AssemblyName 'MapleInputProbe' 'Input probe assembly name'
Assert-Equal $properties.ApplicationManifest 'app.manifest' 'Input probe manifest setting'

$projectReferences = @($project.Project.ItemGroup.ProjectReference | ForEach-Object { $_.Include.Replace('/', '\') })
foreach ($requiredReference in @(
    '..\Maple.Input\Maple.Input.csproj',
    '..\Maple.Contracts\Maple.Contracts.csproj',
    '..\Maple.Core\Maple.Core.csproj'
)) {
    if ($projectReferences -cnotcontains $requiredReference) {
        throw "Input probe project reference is missing: $requiredReference"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
Assert-Contains $manifest 'level="requireAdministrator"' 'Input probe manifest elevation policy'
Assert-Contains $manifest '<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>' 'Input probe manifest DPI policy'

$spec = Get-Content -LiteralPath $specPath -Raw -Encoding UTF8
$handoff = Get-Content -LiteralPath $handoffPath -Raw -Encoding UTF8
$inputContractSection = Get-TextBetween $spec '## 10.' '## 11.' 'Maple input contract section'
$windowsSequenceSection = Get-TextBetween $handoff '## 5.' '## 6.' 'Windows handoff sequence section'
foreach ($requiredText in @('keybd_event', 'diagnostic-only', 'Windows', 'HID', 'L4/L5')) {
    Assert-Contains $inputContractSection $requiredText 'Maple project specification diagnostic-only boundary'
}
foreach ($requiredText in @('keybd_event', 'diagnostic-only', 'Host', 'HID', 'L4/L5')) {
    Assert-Contains $windowsSequenceSection $requiredText 'Windows handoff diagnostic probe milestone'
}

$forbiddenApis = @('SendInput', 'PostMessage', 'mouse_event', 'WriteProcessMemory', 'VirtualProtect')
$probeSources = @(Get-ChildItem -LiteralPath $probeDirectory -Filter '*.cs' -File -Recurse)
if ($probeSources.Count -eq 0) {
    throw "Input probe must contain at least one C# source file: $probeDirectory"
}
$combinedProbeSource = ''
foreach ($source in $probeSources) {
    $sourceText = Get-Content -LiteralPath $source.FullName -Raw -Encoding UTF8
    $combinedProbeSource += $sourceText
    foreach ($forbiddenApi in $forbiddenApis) {
        if ($sourceText.IndexOf($forbiddenApi, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Input probe source contains forbidden API '$forbiddenApi': $($source.FullName)"
        }
    }
}
Assert-Contains $combinedProbeSource 'Application.Run' 'Input probe executable entry point'
Assert-Contains $combinedProbeSource 'Diagnostic-only scaffold' 'Input probe diagnostic-only message'
Assert-Contains $combinedProbeSource 'sends no input' 'Input probe inert-state message'

[xml]$hostProject = Get-Content -LiteralPath $hostProjectPath -Raw -Encoding UTF8
$hostReferences = @($hostProject.Project.ItemGroup.ProjectReference | ForEach-Object { $_.Include })
if ($hostReferences | Where-Object { $_ -match 'Maple\.InputProbe' }) {
    throw 'Maple.Host must not reference Maple.InputProbe.'
}

$solution = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
Assert-Contains $solution 'src\Maple.InputProbe\Maple.InputProbe.csproj' 'Maple solution probe project entry'

Write-Output 'INPUT_PROBE_CONTRACT=PASS'
