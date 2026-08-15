param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts\input-probe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Maple.InputProbe\Maple.InputProbe.csproj'
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
}

dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "Input probe publish failed with exit code $LASTEXITCODE." }

$selfTest = Join-Path $output 'self-test\probe-evidence.jsonl'
dotnet (Join-Path $output 'MapleInputProbe.dll') --self-test --output $selfTest
if ($LASTEXITCODE -ne 0) { throw "Input probe self-test failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $selfTest -PathType Leaf)) { throw "Input probe self-test evidence is missing: $selfTest" }

Write-Output "INPUT_PROBE_BUILD=PASS; OUTPUT=$output; SELF_TEST=$selfTest"
