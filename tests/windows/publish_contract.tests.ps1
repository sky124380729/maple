param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
$publish = if ([IO.Path]::IsPathRooted($PublishDirectory)) {
    [IO.Path]::GetFullPath($PublishDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path (Get-Location) $PublishDirectory))
}
if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
    throw "Publish directory missing: $publish"
}

foreach ($relative in @(
    'Maple.exe',
    'Maple.deps.json',
    'Maple.runtimeconfig.json',
    'WebView2Loader.dll',
    'Vortice.Direct3D11.dll',
    'Vortice.DXGI.dll',
    'coreclr.dll',
    'ui\index.html'
)) {
    $path = Join-Path $publish $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required publish artifact missing: $relative"
    }
}

$javascript = Get-ChildItem -LiteralPath (Join-Path $publish 'ui\assets') -Filter '*.js' -File -ErrorAction SilentlyContinue
if (-not $javascript) {
    throw 'Required publish artifact missing: ui/assets/*.js'
}

$deps = Get-Content -LiteralPath (Join-Path $publish 'Maple.deps.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($deps.runtimeTarget.name -notmatch '/win-x64$') {
    throw "Publish runtime is not win-x64: $($deps.runtimeTarget.name)"
}

Write-Output 'WINDOWS_PUBLISH_CONTRACT=PASS'
