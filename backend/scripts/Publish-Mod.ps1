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

$forbiddenNames = @(
    '0Harmony.dll',
    'Assembly-CSharp.dll',
    'Newtonsoft.Json.dll',
    'LogLibrary.dll',
    'UnityEngine.CoreModule.dll',
    'System.Data.SQLite.dll',
    'SQLite.Interop.dll'
)
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

$forbiddenRootRuntimeNames = @(
    'e_sqlite3.dll',
    'System.Resources.ResourceManager.dll'
)
foreach ($name in $forbiddenRootRuntimeNames) {
    $path = Join-Path $publishPath $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Write-Host "Removing SQLite runtime asset from the Mod root: $name"
        Remove-Item -LiteralPath $path -Force
    }
    if (Test-Path -LiteralPath $path) {
        throw "A SQLite runtime asset must not remain in the Mod root: $path"
    }
}

$obsoleteRuntimeInformationPath = Join-Path $publishPath 'runtimes\win-x64\lib\net45\System.Runtime.InteropServices.RuntimeInformation.dll'
if (Test-Path -LiteralPath $obsoleteRuntimeInformationPath -PathType Leaf) {
    Remove-Item -LiteralPath $obsoleteRuntimeInformationPath -Force
}

$requiredRuntimeAssetPaths = @(
    (Join-Path $publishPath 'runtimes\win-x64\native\e_sqlite3.dll'),
    (Join-Path $publishPath 'runtimes\linux-x64\native\libe_sqlite3.so')
)
$missingRuntimeAssets = $requiredRuntimeAssetPaths |
    Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
if ($missingRuntimeAssets) {
    throw "Missing required SQLite runtime assets: $($missingRuntimeAssets -join ', ')"
}

$requiredNames = @(
    'Dapper.dll',
    'dbup-core.dll',
    'dbup-sqlite.dll',
    'LSTY.SevenDPanel.Adapters.Persistence.Sqlite.dll',
    'Microsoft.CSharp.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'Microsoft.Data.Sqlite.dll',
    'Microsoft.Extensions.DependencyInjection.dll',
    'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
    'Microsoft.Extensions.Logging.Abstractions.dll',
    'Microsoft.Owin.Security.OAuth.dll',
    'SQLitePCLRaw.batteries_v2.dll',
    'SQLitePCLRaw.batteries_v2.dll.config',
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.dynamic_cdecl.dll',
    'System.Buffers.dll',
    'System.ComponentModel.DataAnnotations.dll',
    'System.Dynamic.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Reflection.Emit.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Runtime.InteropServices.RuntimeInformation.dll',
    'System.Threading.Channels.dll',
    'System.Threading.Tasks.Extensions.dll'
)
$missingRequired = $requiredNames | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $publishPath $_) -PathType Leaf)
}
if ($missingRequired) {
    throw "Missing required managed dependencies from publish output: $($missingRequired -join ', ')"
}

$publishPath = [System.IO.Path]::GetFullPath($publishPath)
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

Write-Host "Published Mod output at $publishPath"
