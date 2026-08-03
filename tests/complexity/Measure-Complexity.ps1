[CmdletBinding()]
param([string] $RepositoryRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) { $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition }
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = Join-Path $scriptDirectory '..\..' }
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)

function Get-SourceFiles([string] $Path, [string] $Filter) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $Path -Filter $Filter -File -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|\.pnpm-store|7dtd-reference)\\' } |
        Sort-Object FullName)
}
function Get-LineCount([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 0 }
    return @(Get-Content -LiteralPath $Path).Count
}

$productionRoot = Join-Path $root 'backend\src'
$testRoot = Join-Path $root 'backend\tests'
$adminFeatureRoot = Join-Path $root 'frontend\apps\admin\src\features'
$productionFiles = @(Get-SourceFiles $productionRoot '*.cs')
$testFiles = @(Get-SourceFiles (Join-Path $testRoot 'LSTY.SevenDPanel.Tests') '*.cs')
$productProjects = @(Get-SourceFiles $productionRoot '*.csproj')
$testProjects = @(Get-SourceFiles $testRoot '*.csproj')
$factoryPath = Join-Path $productionRoot 'Bootstrap\LSTY.SevenDPanel\DependencyInjection\PanelServiceProviderFactory.cs'
$navigationPath = Join-Path $root 'frontend\apps\admin\src\app\navigation\navigationCatalog.ts'
$hostingProject = Join-Path $productionRoot 'Runtime\LSTY.SevenDPanel.Hosting\LSTY.SevenDPanel.Hosting.csproj'
$hostingReferencesApplication = 0
if (Test-Path -LiteralPath $hostingProject -PathType Leaf) {
    $hostingReferencesApplication = @((Get-Content -LiteralPath $hostingProject -Raw) | Select-String -Pattern 'ProjectReference[^>]*Application|Application[^<]*\.csproj' -AllMatches).Count
}

$adminFeatures = @()
if (Test-Path -LiteralPath $adminFeatureRoot -PathType Container) {
    $adminFeatures = @(Get-ChildItem -LiteralPath $adminFeatureRoot -Directory | Where-Object { $_.Name -notmatch '^(\.git|__tests__)$' } | Sort-Object Name)
}

$navigationGroups = 0
if (Test-Path -LiteralPath $navigationPath -PathType Leaf) {
    $navigationSource = Get-Content -LiteralPath $navigationPath -Raw
    $groupsSection = ($navigationSource -split '(?m)^\s*routeParents\s*:')[0]
    $navigationGroups = @([regex]::Matches($groupsSection, "(?m)^\s{6}id:\s*'[^']+'")).Count
}

$compositionRootCount = 0
foreach ($file in $productionFiles) {
    $compositionRootCount += @([regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw), 'BuildServiceProvider\s*\(')).Count
}

$unknownCapabilityCount = 0
$maturityDocument = Join-Path $root 'docs\test.md'
if (Test-Path -LiteralPath $maturityDocument -PathType Leaf) {
    $maturitySource = Get-Content -LiteralPath $maturityDocument -Raw
    $maturityMatch = [regex]::Match($maturitySource, '(?s)<!--\s*CAPABILITY_MATURITY_START\s*-->(?<table>.*?)<!--\s*CAPABILITY_MATURITY_END\s*-->')
    if ($maturityMatch.Success) {
        $validOwners = @('Platform','Operations','Players','Community','Economy','Automation','Administration')
        foreach ($line in @($maturityMatch.Groups['table'].Value -split "`r?`n" | Where-Object { $_ -match '^\|' } | Select-Object -Skip 2)) {
            $fields = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
            if ($fields.Count -ge 2 -and $validOwners -notcontains $fields[1]) { $unknownCapabilityCount++ }
        }
    }
}

$newPublicInterfaceCount = 0
try {
    $diff = @(& git -C $root diff --unified=0 -- '*.cs' 2>$null)
    if ($LASTEXITCODE -eq 0) { $newPublicInterfaceCount = @($diff | Where-Object { $_ -match '^\+\s*public\s+.*\binterface\b' }).Count }
}
catch { $newPublicInterfaceCount = 0 }

$result = [ordered]@{
    schemaVersion = 1
    measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    productionCsFileCount = $productionFiles.Count
    productProjectCount = $productProjects.Count
    backendTestProjectCount = $testProjects.Count
    backendTestFileCount = $testFiles.Count
    adminFeatureCount = $adminFeatures.Count
    firstLevelNavigationTaskCount = $navigationGroups
    bootstrapRegistrationLineCount = Get-LineCount $factoryPath
    compositionRootCount = $compositionRootCount
    hostingApplicationProjectReferences = $hostingReferencesApplication
    unknownCapabilityCount = $unknownCapabilityCount
    newPublicInterfaceCount = $newPublicInterfaceCount
}

$result | ConvertTo-Json -Depth 5 -Compress
