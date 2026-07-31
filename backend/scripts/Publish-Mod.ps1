[CmdletBinding()]
param(
    [string] $EnvironmentFile
)

$ErrorActionPreference = 'Stop'
if (-not $EnvironmentFile) { $EnvironmentFile = Join-Path $PSScriptRoot '..\.env.local' }
$environment = @{}
if (Test-Path -LiteralPath $EnvironmentFile) {
    Get-Content -LiteralPath $EnvironmentFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#')) {
            $parts = $line -split '=', 2
            if ($parts.Count -eq 2) { $environment[$parts[0].Trim()] = $parts[1].Trim().Trim('"', "'") }
        }
    }
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'backend\src\Bootstrap\LSTY.SevenDPanel\LSTY.SevenDPanel.csproj'
$projectDirectory = Split-Path $project -Parent
$adminDistPath = Join-Path $repoRoot 'frontend\apps\admin\dist'
$adminIndexPath = Join-Path $adminDistPath 'index.html'
$adminAssetsPath = Join-Path $adminDistPath 'assets'
if (-not (Test-Path -LiteralPath $adminIndexPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $adminAssetsPath -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $adminAssetsPath -File | Select-Object -First 1)) {
    throw 'Admin build output is missing or incomplete. Run pnpm build in frontend/apps/admin before publishing.'
}

$configuredPublishPath = $environment['SEVENDPANEL_PUBLISH_DIR']
if ($configuredPublishPath) {
    $publishPath = if ([System.IO.Path]::IsPathRooted($configuredPublishPath)) {
        [System.IO.Path]::GetFullPath($configuredPublishPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $configuredPublishPath))
    }
    dotnet publish $project --configuration Release --no-restore -p:PublishProfile=FolderProfile -p:PublishDir="$publishPath\"
}
else {
    $publishPath = Join-Path $projectDirectory 'bin\Release\net48\publish'
    dotnet publish $project --configuration Release --no-restore -p:PublishProfile=FolderProfile
}

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishPath = [System.IO.Path]::GetFullPath($publishPath).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$publishPrefix = $publishPath + [System.IO.Path]::DirectorySeparatorChar
$releaseManifestPath = Join-Path $PSScriptRoot 'release-manifest.json'
$releaseManifest = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json
$forbiddenNames = @($releaseManifest.forbiddenFileNames)
$forbiddenFiles = Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object {
    $_.Name -in $forbiddenNames
}
if ($forbiddenFiles) {
    Write-Host "Removing forbidden assemblies from publish output: $($forbiddenFiles.Name -join ', ')"
    $forbiddenFiles | Remove-Item -Force
}

$forbidden = Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object {
    $_.Name -in $forbiddenNames
}
if ($forbidden) {
    throw "The publish directory contains forbidden assemblies: $($forbidden.Name -join ', ')"
}

foreach ($relativePath in @($releaseManifest.forbiddenRelativePaths)) {
    $normalizedRelativePath = ([string]$relativePath).Replace('\', '/')
    $segments = @($normalizedRelativePath.Split('/') | Where-Object { $_.Length -gt 0 })
    if ([System.IO.Path]::IsPathRooted($relativePath) -or
        $segments.Count -eq 0 -or
        @($segments | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0) {
        throw "The release manifest contains an unsafe forbidden path: $relativePath"
    }
    $path = [System.IO.Path]::GetFullPath((Join-Path $publishPath $normalizedRelativePath.Replace(
        '/',
        [System.IO.Path]::DirectorySeparatorChar)))
    if (-not $path.StartsWith($publishPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The release manifest forbidden path escapes publish output: $relativePath"
    }
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Write-Host "Removing forbidden asset from publish output: $relativePath"
        Remove-Item -LiteralPath $path -Force
    }
    if (Test-Path -LiteralPath $path) {
        throw "A forbidden asset must not remain in publish output: $path"
    }
}

$wwwrootPath = [System.IO.Path]::GetFullPath((Join-Path $publishPath 'wwwroot'))
if ((Split-Path $wwwrootPath -Parent) -ne $publishPath) {
    throw "Refusing to replace an unexpected Admin asset path: $wwwrootPath"
}

if (Test-Path -LiteralPath $wwwrootPath) {
    Remove-Item -LiteralPath $wwwrootPath -Recurse -Force
}
New-Item -ItemType Directory -Path $wwwrootPath | Out-Null
Get-ChildItem -LiteralPath $adminDistPath -Force | Copy-Item -Destination $wwwrootPath -Recurse -Force

$publishedIndexPath = Join-Path $wwwrootPath 'index.html'
$publishedAssetsPath = Join-Path $wwwrootPath 'assets'
if (-not (Test-Path -LiteralPath $publishedIndexPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $publishedAssetsPath -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $publishedAssetsPath -File | Select-Object -First 1)) {
    throw "Published Admin assets are incomplete under $wwwrootPath"
}

& (Join-Path $PSScriptRoot 'Test-ReleaseArtifact.ps1') `
    -ArtifactPath $publishPath `
    -ManifestPath $releaseManifestPath

Write-Host "Published Mod output at $publishPath"
