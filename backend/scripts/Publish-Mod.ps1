[CmdletBinding()]
param(
    [string] $EnvironmentFile,
    [string] $PublishDirectory
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
$playerDistPath = Join-Path $repoRoot 'frontend\apps\player\dist'
$playerIndexPath = Join-Path $playerDistPath 'index.html'
$playerAssetsPath = Join-Path $playerDistPath 'assets'
if (-not (Test-Path -LiteralPath $adminIndexPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $adminAssetsPath -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $adminAssetsPath -File | Select-Object -First 1)) {
    throw 'Admin build output is missing or incomplete. Run pnpm build in frontend/apps/admin before publishing.'
}
if (-not (Test-Path -LiteralPath $playerIndexPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $playerAssetsPath -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $playerAssetsPath -File | Select-Object -First 1)) {
    throw 'Player build output is missing or incomplete. Run pnpm build in frontend/apps/player before publishing.'
}

$configuredPublishPath = if ($PSBoundParameters.ContainsKey('PublishDirectory')) {
    if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
        throw 'PublishDirectory cannot be empty when explicitly provided.'
    }
    $PublishDirectory
}
else {
    $environment['SEVENDPANEL_PUBLISH_DIR']
}
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
$releaseManifestPath = Join-Path $PSScriptRoot 'release-manifest.json'
& (Join-Path $PSScriptRoot 'Remove-ForbiddenReleaseArtifactContent.ps1') `
    -ArtifactPath $publishPath `
    -ManifestPath $releaseManifestPath

$wwwrootPath = [System.IO.Path]::GetFullPath((Join-Path $publishPath 'wwwroot'))
if ((Split-Path $wwwrootPath -Parent) -ne $publishPath) {
    throw "Refusing to replace an unexpected Admin asset path: $wwwrootPath"
}

if (Test-Path -LiteralPath $wwwrootPath) {
    Remove-Item -LiteralPath $wwwrootPath -Recurse -Force
}
New-Item -ItemType Directory -Path $wwwrootPath | Out-Null
Get-ChildItem -LiteralPath $adminDistPath -Force | Copy-Item -Destination $wwwrootPath -Recurse -Force
$publishedPlayerPath = Join-Path $wwwrootPath 'player'
New-Item -ItemType Directory -Path $publishedPlayerPath | Out-Null
Get-ChildItem -LiteralPath $playerDistPath -Force | Copy-Item -Destination $publishedPlayerPath -Recurse -Force

$publishedIndexPath = Join-Path $wwwrootPath 'index.html'
$publishedAssetsPath = Join-Path $wwwrootPath 'assets'
$publishedPlayerIndexPath = Join-Path $publishedPlayerPath 'index.html'
$publishedPlayerAssetsPath = Join-Path $publishedPlayerPath 'assets'
if (-not (Test-Path -LiteralPath $publishedIndexPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $publishedAssetsPath -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $publishedAssetsPath -File | Select-Object -First 1)) {
    throw "Published Admin assets are incomplete under $wwwrootPath"
}
if (-not (Test-Path -LiteralPath $publishedPlayerIndexPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $publishedPlayerAssetsPath -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $publishedPlayerAssetsPath -File | Select-Object -First 1)) {
    throw "Published Player assets are incomplete under $publishedPlayerPath"
}

& (Join-Path $PSScriptRoot 'Test-ReleaseArtifact.ps1') `
    -ArtifactPath $publishPath `
    -ManifestPath $releaseManifestPath

Write-Host "Published Mod output at $publishPath"
