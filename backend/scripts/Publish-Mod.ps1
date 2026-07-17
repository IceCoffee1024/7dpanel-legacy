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
$project = Join-Path $repoRoot 'backend\src\LSTY.SevenDPanel\LSTY.SevenDPanel.csproj'
$projectDirectory = Split-Path $project -Parent
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

$forbiddenNames = @('Assembly-CSharp.dll', 'Newtonsoft.Json.dll', 'LogLibrary.dll', 'System.Runtime.CompilerServices.Unsafe.dll')
$gameAssemblies = Get-ChildItem -LiteralPath $publishPath -File | Where-Object {
    $_.Name -in $forbiddenNames
}
if ($gameAssemblies) {
    Write-Host "Removing game-provided assemblies from publish output: $($gameAssemblies.Name -join ', ')"
    $gameAssemblies | Remove-Item -Force
}

$forbidden = Get-ChildItem -LiteralPath $publishPath -File | Where-Object {
    $_.Name -in $forbiddenNames
}
if ($forbidden) {
    throw "The publish directory contains game-provided assemblies: $($forbidden.Name -join ', ')"
}

Write-Host "Published Mod output at $publishPath"
