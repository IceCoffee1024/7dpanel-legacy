[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactPath,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'release-manifest.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Get-RequiredArray {
    param(
        [Parameter(Mandatory = $true)] [object] $Object,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Release manifest property '$Name' is missing."
    }
    $values = @($property.Value)
    if ($values.Count -eq 0 -or
        @($values | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "Release manifest property '$Name' must be a non-empty string array."
    }
    return $values
}

function Assert-RelativePath {
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

function Get-ArtifactPath {
    param([Parameter(Mandatory = $true)] [string] $RelativePath)

    $nativeRelativePath = $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $nativeRelativePath))
    if (-not $candidate.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release manifest path escapes the artifact root: $RelativePath"
    }
    return $candidate
}

function Assert-UniqueRelativePaths {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Values,
        [Parameter(Mandatory = $true)] [string] $PropertyName
    )

    $seen = @{}
    foreach ($value in $Values) {
        $normalized = Assert-RelativePath $value $PropertyName
        $key = $normalized.ToUpperInvariant()
        if ($seen.ContainsKey($key)) {
            throw "Release manifest property '$PropertyName' contains a duplicate path: $value"
        }
        $seen[$key] = $true
    }
}

$resolvedManifest = Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedManifest.ProviderPath -PathType Leaf)) {
    throw "Release manifest path is not a file: $ManifestPath"
}
try {
    $manifest = Get-Content -LiteralPath $resolvedManifest.ProviderPath -Raw | ConvertFrom-Json
}
catch {
    throw "Release manifest is not valid JSON: $($resolvedManifest.ProviderPath)"
}
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported release manifest schema version: $($manifest.schemaVersion)"
}

$expectedProductAssemblies = @(
    'LSTY.SevenDPanel.dll',
    'LSTY.SevenDPanel.Application.dll',
    'LSTY.SevenDPanel.Domain.dll',
    'LSTY.SevenDPanel.Hosting.dll',
    'LSTY.SevenDPanel.Adapters.Local.dll',
    'LSTY.SevenDPanel.Adapters.Persistence.Sqlite.dll',
    'LSTY.SevenDPanel.Adapters.SevenDays.dll',
    'LSTY.SevenDPanel.Adapters.Web.dll'
)
$productAssemblies = @(Get-RequiredArray $manifest 'productAssemblies')
if ($productAssemblies.Count -ne $expectedProductAssemblies.Count -or
    (Compare-Object $expectedProductAssemblies $productAssemblies -CaseSensitive)) {
    throw 'Release manifest must contain exactly the eight current product assemblies.'
}

$requiredManagedAssemblies = @(Get-RequiredArray $manifest 'requiredManagedAssemblies')
$requiredFiles = @(Get-RequiredArray $manifest 'requiredFiles')
$requiredNativeAssets = @(Get-RequiredArray $manifest 'requiredNativeAssets')
$forbiddenFileNames = @(Get-RequiredArray $manifest 'forbiddenFileNames')
$forbiddenRelativePaths = @(Get-RequiredArray $manifest 'forbiddenRelativePaths')
$forbiddenPathSegments = @(Get-RequiredArray $manifest 'forbiddenPathSegments')
$configExamples = @(Get-RequiredArray $manifest 'configExamples')

foreach ($entry in @(
    @{ Values = $productAssemblies; Name = 'productAssemblies' },
    @{ Values = $requiredManagedAssemblies; Name = 'requiredManagedAssemblies' },
    @{ Values = $requiredFiles; Name = 'requiredFiles' },
    @{ Values = $requiredNativeAssets; Name = 'requiredNativeAssets' },
    @{ Values = $forbiddenRelativePaths; Name = 'forbiddenRelativePaths' },
    @{ Values = $configExamples; Name = 'configExamples' }
)) {
    Assert-UniqueRelativePaths $entry.Values $entry.Name
}

$resolvedArtifact = Resolve-Path -LiteralPath $ArtifactPath -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedArtifact.ProviderPath -PathType Container)) {
    throw "Release artifact path is not a directory: $ArtifactPath"
}
$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($resolvedArtifact.ProviderPath)
$artifactVolumeRoot = [System.IO.Path]::GetPathRoot($resolvedArtifactRoot)
if ($resolvedArtifactRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) -eq
    $artifactVolumeRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)) {
    throw "Release artifact path must not be a filesystem root: $ArtifactPath"
}
$artifactRootItem = Get-Item -LiteralPath $resolvedArtifactRoot -Force
if ($artifactRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
    throw "Release artifact root must not be a reparse point: $ArtifactPath"
}
$artifactRoot = $resolvedArtifactRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$artifactPrefix = $artifactRoot + [System.IO.Path]::DirectorySeparatorChar

$artifactEntries = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -Force)
$reparsePoints = @($artifactEntries | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint })
if ($reparsePoints.Count -gt 0) {
    throw "Release artifact contains reparse points: $($reparsePoints.FullName -join ', ')"
}

$artifactFiles = @($artifactEntries | Where-Object { -not $_.PSIsContainer })
foreach ($segment in $forbiddenPathSegments) {
    if ($segment.IndexOfAny(@([char]'/', [char]'\')) -ge 0) {
        throw "Release manifest property 'forbiddenPathSegments' contains a path: $segment"
    }
    $matchingEntries = @($artifactEntries | Where-Object {
        $_.FullName.Substring($artifactPrefix.Length).Split(@([char]'/', [char]'\')) -contains $segment
    })
    if ($matchingEntries.Count -gt 0) {
        throw "Release artifact contains forbidden path segment '$segment': $($matchingEntries.FullName -join ', ')"
    }
}

$forbiddenFiles = @($artifactFiles | Where-Object { $forbiddenFileNames -contains $_.Name })
if ($forbiddenFiles.Count -gt 0) {
    throw "Release artifact contains forbidden assemblies or assets: $($forbiddenFiles.FullName -join ', ')"
}

foreach ($relativePath in $forbiddenRelativePaths) {
    $path = Get-ArtifactPath $relativePath
    if (Test-Path -LiteralPath $path) {
        throw "Release artifact contains forbidden path: $relativePath"
    }
}

foreach ($nativeAssetName in @($requiredNativeAssets | ForEach-Object {
    [System.IO.Path]::GetFileName($_)
} | Select-Object -Unique)) {
    $allowedNativePaths = @($requiredNativeAssets | Where-Object {
        [System.IO.Path]::GetFileName($_) -eq $nativeAssetName
    } | ForEach-Object { (Assert-RelativePath $_ 'requiredNativeAssets').ToUpperInvariant() })
    $misplacedNativeAssets = @($artifactFiles | Where-Object { $_.Name -eq $nativeAssetName } | Where-Object {
        $relativePath = $_.FullName.Substring($artifactPrefix.Length).Replace('\', '/')
        $allowedNativePaths -notcontains $relativePath.ToUpperInvariant()
    })
    if ($misplacedNativeAssets.Count -gt 0) {
        throw "Release artifact contains misplaced native assets: $($misplacedNativeAssets.FullName -join ', ')"
    }
}

$requiredArtifactFiles = @($productAssemblies + $requiredManagedAssemblies + $requiredFiles + $requiredNativeAssets)
Assert-UniqueRelativePaths $requiredArtifactFiles 'combined required files'
$missingFiles = @($requiredArtifactFiles | Where-Object {
    -not (Test-Path -LiteralPath (Get-ArtifactPath $_) -PathType Leaf)
})
if ($missingFiles.Count -gt 0) {
    throw "Release artifact is missing required files: $($missingFiles -join ', ')"
}

$adminProperty = $manifest.PSObject.Properties['admin']
if ($null -eq $adminProperty -or $null -eq $adminProperty.Value) {
    throw "Release manifest property 'admin' is missing."
}
$admin = $adminProperty.Value
$adminRoot = Assert-RelativePath ([string]$admin.root) 'admin.root'
$adminIndex = Assert-RelativePath ([string]$admin.index) 'admin.index'
$adminAssetsDirectory = Assert-RelativePath ([string]$admin.assetsDirectory) 'admin.assetsDirectory'
if (-not $adminIndex.StartsWith($adminRoot + '/', [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $adminAssetsDirectory.StartsWith($adminRoot + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release manifest Admin paths must be contained by admin.root.'
}
if (-not (Test-Path -LiteralPath (Get-ArtifactPath $adminIndex) -PathType Leaf)) {
    throw "Release artifact Admin index is missing: $adminIndex"
}
$adminAssetsPath = Get-ArtifactPath $adminAssetsDirectory
if (-not (Test-Path -LiteralPath $adminAssetsPath -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $adminAssetsPath -Recurse -File | Select-Object -First 1)) {
    throw "Release artifact Admin assets are missing or empty: $adminAssetsDirectory"
}

foreach ($configExample in $configExamples) {
    $configPath = Get-ArtifactPath $configExample
    try {
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Release artifact config example is not valid JSON: $configExample"
    }
    if ($null -eq $config -or $config -isnot [PSCustomObject]) {
        throw "Release artifact config example must contain a JSON object: $configExample"
    }
}

Write-Output "Release artifact validation passed: $artifactRoot"
