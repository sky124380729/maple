param(
    [Parameter(Mandatory = $true)]
    [string]$ModelPath,
    [string]$ModelId = "kaelo-maple-yolo",
    [string]$Version = "local-agpl-3.0",
    [double]$DisplayConfidenceThreshold = 0.25,
    [string]$ManifestPath
)

$ErrorActionPreference = "Stop"
$resolvedModel = (Resolve-Path -LiteralPath $ModelPath).Path
$hash = (Get-FileHash -LiteralPath $resolvedModel -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestPath = if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    Join-Path $env:LOCALAPPDATA "Maple\models\active\manifest.json"
}
else {
    [IO.Path]::GetFullPath($ManifestPath)
}
$manifestDirectory = Split-Path -Parent $manifestPath
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null

$manifest = [ordered]@{
    schemaVersion = 2
    modelId = $ModelId
    version = $Version
    modelFile = $resolvedModel
    sha256 = $hash
    runtime = "onnx"
    inputWidth = 320
    inputHeight = 320
    confidenceThreshold = 0.60
    displayConfidenceThreshold = $DisplayConfidenceThreshold
    nmsThreshold = 0.45
    classes = @("character", "environment", "item", "mob", "npc", "ui")
    classRoles = [ordered]@{
        character = "characterCandidate"
        environment = "ignore"
        item = "ignore"
        mob = "monster"
        npc = "ignore"
        ui = "ignore"
    }
    outputLayout = "yoloChannelsFirst"
}

$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($manifestPath, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "MODEL_MANIFEST_CONFIGURED=$manifestPath"
Write-Output "MODEL_SHA256=$hash"
