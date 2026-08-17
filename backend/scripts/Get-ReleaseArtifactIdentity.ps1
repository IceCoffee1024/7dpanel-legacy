[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactPath,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'release-manifest.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Get-RequiredArray {
    param([object] $Object, [string] $Name)

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Release manifest property '$Name' is missing." }
    $values = @($property.Value)
    if ($values.Count -eq 0 -or @($values | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "Release manifest property '$Name' must be a non-empty string array."
    }
    return $values
}

function Assert-RelativePath {
    param([string] $Value, [string] $PropertyName)

    $normalized = $Value.Replace('\', '/')
    $segments = @($normalized.Split('/') | Where-Object { $_.Length -gt 0 })
    if ([System.IO.Path]::IsPathRooted($Value) -or $normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:' -or
        $normalized.Contains(':') -or $segments.Count -eq 0 -or @($segments | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0) {
        throw "Release manifest property '$PropertyName' contains an unsafe relative path: $Value"
    }
    return $normalized
}

function Get-Sha256Hex {
    param([byte[]] $Bytes)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($algorithm.ComputeHash($Bytes) | ForEach-Object { $_.ToString('X2') }) -join '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-FileSha256Hex {
    param([string] $Path)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return (($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString('X2') }) -join '')
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

$resolvedManifest = Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop
try {
    $manifest = Get-Content -LiteralPath $resolvedManifest.ProviderPath -Raw | ConvertFrom-Json
}
catch {
    throw "Release manifest is not valid JSON: $($resolvedManifest.ProviderPath)"
}
if ($manifest.schemaVersion -ne 1) { throw "Unsupported release manifest schema version: $($manifest.schemaVersion)" }

$approvedFiles = @(
    (Get-RequiredArray $manifest 'productAssemblies') +
    (Get-RequiredArray $manifest 'requiredManagedAssemblies') +
    (Get-RequiredArray $manifest 'requiredFiles') +
    (Get-RequiredArray $manifest 'requiredNativeAssets')
)
$admin = $manifest.PSObject.Properties['admin'].Value
if ($null -eq $admin) { throw "Release manifest property 'admin' is missing." }
$adminIndex = Assert-RelativePath ([string] $admin.index) 'admin.index'
$adminAssetsDirectory = Assert-RelativePath ([string] $admin.assetsDirectory) 'admin.assetsDirectory'
$approvedFiles += $adminIndex
$player = $manifest.PSObject.Properties['player'].Value
if ($null -eq $player) { throw "Release manifest property 'player' is missing." }
$playerIndex = Assert-RelativePath ([string] $player.index) 'player.index'
$playerAssetsDirectory = Assert-RelativePath ([string] $player.assetsDirectory) 'player.assetsDirectory'
$approvedFiles += $playerIndex

$deploymentState = $manifest.PSObject.Properties['deploymentState'].Value
if ($null -eq $deploymentState) { throw "Release manifest property 'deploymentState' is missing." }
$deploymentStateFiles = @(Get-RequiredArray $deploymentState 'files' | ForEach-Object {
    Assert-RelativePath $_ 'deploymentState.files'
})
$deploymentStateDirectories = @(Get-RequiredArray $deploymentState 'directories' | ForEach-Object {
    Assert-RelativePath $_ 'deploymentState.directories'
})

$approvedByPath = @{}
foreach ($file in $approvedFiles) {
    $normalized = Assert-RelativePath $file 'approved files'
    $key = $normalized.ToUpperInvariant()
    if ($approvedByPath.ContainsKey($key)) { throw "Release manifest contains a duplicate approved file path: $normalized" }
    $approvedByPath[$key] = $true
}

$deploymentStateFilesByPath = @{}
foreach ($file in $deploymentStateFiles) {
    $key = $file.ToUpperInvariant()
    if ($deploymentStateFilesByPath.ContainsKey($key)) { throw "Release manifest contains a duplicate deployment state file path: $file" }
    if ($approvedByPath.ContainsKey($key)) { throw "Release manifest deployment state overlaps an approved file path: $file" }
    $deploymentStateFilesByPath[$key] = $true
}

$deploymentStateDirectoryPrefixes = @()
foreach ($directory in $deploymentStateDirectories) {
    $prefix = $directory.TrimEnd('/') + '/'
    $key = $prefix.ToUpperInvariant()
    if (@($deploymentStateDirectoryPrefixes | Where-Object { $_.ToUpperInvariant() -eq $key }).Count -gt 0) {
        throw "Release manifest contains a duplicate deployment state directory path: $directory"
    }
    if (@($approvedByPath.Keys | Where-Object { $_ -eq $directory.ToUpperInvariant() -or $_.StartsWith($key) }).Count -gt 0) {
        throw "Release manifest deployment state directory overlaps an approved file path: $directory"
    }
    $deploymentStateDirectoryPrefixes += $prefix
}

$inputArtifact = Get-Item -LiteralPath $ArtifactPath -Force -ErrorAction Stop
if ($inputArtifact.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Release artifact root must not be a reparse point: $ArtifactPath" }
$resolvedArtifact = Resolve-Path -LiteralPath $ArtifactPath -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedArtifact.ProviderPath -PathType Container)) { throw "Release artifact path is not a directory: $ArtifactPath" }
$artifactRoot = [System.IO.Path]::GetFullPath($resolvedArtifact.ProviderPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$volumeRoot = [System.IO.Path]::GetPathRoot($artifactRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if ($artifactRoot -eq $volumeRoot) { throw "Release artifact path must not be a filesystem root: $ArtifactPath" }
$rootItem = Get-Item -LiteralPath $artifactRoot -Force
if ($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Release artifact root must not be a reparse point: $ArtifactPath" }

$prefix = $artifactRoot + [System.IO.Path]::DirectorySeparatorChar
$entries = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -Force)
$reparsePoints = @($entries | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint })
if ($reparsePoints.Count -gt 0) { throw "Release artifact contains reparse points: $($reparsePoints.FullName -join ', ')" }

$assetPrefixes = @(
    ($adminAssetsDirectory + '/')
    ($playerAssetsDirectory + '/')
)
$identityFiles = @()
foreach ($file in @($entries | Where-Object { -not $_.PSIsContainer })) {
    $relativePath = $file.FullName.Substring($prefix.Length).Replace('\', '/')
    $key = $relativePath.ToUpperInvariant()
    $isDeploymentState = $deploymentStateFilesByPath.ContainsKey($key)
    if (-not $isDeploymentState) {
        $isDeploymentState = @($deploymentStateDirectoryPrefixes | Where-Object {
            $relativePath.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
    }
    if ($isDeploymentState) { continue }
    $isApprovedAsset = @($assetPrefixes | Where-Object {
        $relativePath.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase)
    }).Count -gt 0
    if (-not $approvedByPath.ContainsKey($key) -and -not $isApprovedAsset) {
        throw "Release artifact file is not approved by the release manifest: $relativePath"
    }
    $fileHash = Get-FileSha256Hex $file.FullName
    $identityFiles += [pscustomobject]([ordered]@{
        path = $relativePath
        length = [Int64] $file.Length
        sha256 = $fileHash
    })
}

foreach ($approvedPath in $approvedByPath.Keys) {
    if (@($identityFiles | Where-Object { $_.path.ToUpperInvariant() -eq $approvedPath }).Count -ne 1) {
        throw "Release artifact is missing approved file: $approvedPath"
    }
}
if (@($identityFiles | Where-Object { $_.path.StartsWith($assetPrefixes[0], [System.StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) {
    throw "Release artifact Admin assets are missing or empty: $adminAssetsDirectory"
}
if (@($identityFiles | Where-Object { $_.path.StartsWith($assetPrefixes[1], [System.StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) {
    throw "Release artifact Player assets are missing or empty: $playerAssetsDirectory"
}

$orderedFiles = @($identityFiles | Sort-Object -Property path)
$canonical = New-Object System.Text.StringBuilder
foreach ($file in $orderedFiles) {
    [void] $canonical.Append($file.path).Append("`n")
    [void] $canonical.Append($file.length.ToString([System.Globalization.CultureInfo]::InvariantCulture)).Append("`n")
    [void] $canonical.Append($file.sha256).Append("`n")
}

[pscustomobject]([ordered]@{
    schemaVersion = 1
    artifactSha256 = Get-Sha256Hex ([System.Text.Encoding]::UTF8.GetBytes($canonical.ToString()))
    files = $orderedFiles
})
