[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactPath,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'release-manifest.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function ConvertTo-SafeRelativePath {
    param(
        [Parameter(Mandatory = $true)] [string] $Value,
        [Parameter(Mandatory = $true)] [string] $PropertyName
    )

    $normalized = $Value.Replace('\', '/')
    $segments = @($normalized.Split('/') | Where-Object { $_.Length -gt 0 })
    if ([System.IO.Path]::IsPathRooted($Value) -or
        $normalized.StartsWith('/') -or
        $normalized -match '^[A-Za-z]:' -or
        $normalized.Contains(':') -or
        $segments.Count -eq 0 -or
        @($segments | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0) {
        throw "Release manifest property '$PropertyName' contains an unsafe relative path: $Value"
    }
    return $normalized
}

function Get-ArtifactFilePath {
    param([Parameter(Mandatory = $true)] [string] $RelativePath)

    $normalized = ConvertTo-SafeRelativePath $RelativePath 'release artifact cleanup paths'
    $nativeRelativePath = $normalized.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $nativeRelativePath))
    if (-not $candidate.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release manifest path escapes the artifact root: $RelativePath"
    }
    return $candidate
}

$resolvedManifest = Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop
$manifest = Get-Content -LiteralPath $resolvedManifest.ProviderPath -Raw | ConvertFrom-Json
$resolvedArtifact = Resolve-Path -LiteralPath $ArtifactPath -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedArtifact.ProviderPath -PathType Container)) {
    throw "Release artifact path is not a directory: $ArtifactPath"
}

$artifactRoot = [System.IO.Path]::GetFullPath($resolvedArtifact.ProviderPath).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$volumeRoot = [System.IO.Path]::GetPathRoot($artifactRoot).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if ($artifactRoot -eq $volumeRoot) {
    throw "Release artifact path must not be a filesystem root: $ArtifactPath"
}
$artifactRootItem = Get-Item -LiteralPath $artifactRoot -Force
if ($artifactRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
    throw "Release artifact root must not be a reparse point: $ArtifactPath"
}
$artifactPrefix = $artifactRoot + [System.IO.Path]::DirectorySeparatorChar
$artifactEntries = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -Force)
$reparsePoints = @($artifactEntries | Where-Object {
    $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint
})
if ($reparsePoints.Count -gt 0) {
    throw "Release artifact contains reparse points: $($reparsePoints.FullName -join ', ')"
}

$forbiddenNames = @($manifest.forbiddenFileNames)
if ($forbiddenNames.Count -eq 0 -or @($forbiddenNames | Where-Object {
    $_ -isnot [string] -or
    [string]::IsNullOrWhiteSpace($_) -or
    [System.IO.Path]::GetFileName($_) -ne $_
}).Count -gt 0) {
    throw "Release manifest property 'forbiddenFileNames' must contain file names."
}

$removed = [System.Collections.Generic.List[string]]::new()
$artifactFiles = @($artifactEntries | Where-Object { -not $_.PSIsContainer })
foreach ($file in @($artifactFiles | Where-Object { $_.Name -in $forbiddenNames })) {
    $removed.Add($file.FullName.Substring($artifactPrefix.Length).Replace('\', '/'))
    Remove-Item -LiteralPath $file.FullName -Force
}

foreach ($relativePath in @($manifest.forbiddenRelativePaths)) {
    $path = Get-ArtifactFilePath ([string]$relativePath)
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $removed.Add(([string]$relativePath).Replace('\', '/'))
        Remove-Item -LiteralPath $path -Force
    }
    elseif (Test-Path -LiteralPath $path) {
        throw "A forbidden release path is not a file: $relativePath"
    }
}

$requiredNativeAssets = @($manifest.requiredNativeAssets)
foreach ($nativeAssetName in @($requiredNativeAssets | ForEach-Object {
    [System.IO.Path]::GetFileName([string]$_)
} | Select-Object -Unique)) {
    $allowedPaths = @($requiredNativeAssets | Where-Object {
        [System.IO.Path]::GetFileName([string]$_) -eq $nativeAssetName
    } | ForEach-Object {
        (ConvertTo-SafeRelativePath ([string]$_) 'requiredNativeAssets').ToUpperInvariant()
    })
    foreach ($file in @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -File | Where-Object {
        $_.Name -eq $nativeAssetName
    })) {
        $relativePath = $file.FullName.Substring($artifactPrefix.Length).Replace('\', '/')
        if ($allowedPaths -contains $relativePath.ToUpperInvariant()) { continue }
        $removed.Add($relativePath)
        Remove-Item -LiteralPath $file.FullName -Force
    }
}

if ($removed.Count -gt 0) {
    Write-Output "Removed forbidden release artifact content: $($removed -join ', ')"
}
