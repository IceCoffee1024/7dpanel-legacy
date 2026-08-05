[CmdletBinding()]
param([string] $RepositoryRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) { $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition }
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = Join-Path $scriptDirectory '..\..' }
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)

$excludedDirectoryNames = @(
    'bin', 'obj', 'node_modules', '.pnpm-store', '7dtd-reference', 'generated',
    'migration', 'migrations', 'snapshot', 'snapshots', 'artifact', 'artifacts',
    'dist', 'build', 'coverage'
)
$excludedFilePattern = '(?i)(?:^generated[^.]*|\.generated\.|\.gen\.|\.snapshot\.|\.snap$|^route-map\.d\.)'

function Test-ExcludedPath([string] $Path) {
    $normalizedPath = $Path.Replace('/', '\')
    $fileName = [System.IO.Path]::GetFileName($Path)
    foreach ($directoryName in @($normalizedPath -split '\\')) {
        if ($excludedDirectoryNames -contains $directoryName) { return $true }
    }
    return ($fileName -match $excludedFilePattern)
}

function Get-SourceFiles([string] $Path, [string] $Filter) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $Path -Filter $Filter -File -Recurse |
        Where-Object { -not (Test-ExcludedPath $_.FullName) } |
        Sort-Object FullName)
}

function Get-LineCount([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 0 }
    return @([System.IO.File]::ReadAllLines($Path)).Count
}

function Get-FileCountOverThreshold([object[]] $Files, [int] $Threshold) {
    $count = 0
    foreach ($file in @($Files)) {
        if ((Get-LineCount $file.FullName) -gt $Threshold) { $count++ }
    }
    return $count
}

function Get-FeatureName([string] $Path, [string] $FeatureRoot) {
    $normalizedPath = $Path.Replace('\', '/')
    $normalizedRoot = $FeatureRoot.Replace('\', '/').TrimEnd('/')
    $prefix = $normalizedRoot + '/'
    if (-not $normalizedPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $null }
    $relative = $normalizedPath.Substring($prefix.Length)
    return ($relative -split '/')[0]
}

function Get-ImportedFeatureName([string] $Specifier, [string] $ImporterPath, [string] $FeatureRoot) {
    if ($Specifier -match '^(?:@|~)/features/([^/]+)') { return $Matches[1] }
    if ($Specifier -notmatch '^\.') { return $null }

    try {
        $targetPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ImporterPath) $Specifier))
        return Get-FeatureName $targetPath $FeatureRoot
    }
    catch {
        return $null
    }
}

function Get-FeatureInternalCrossDomainImportCount([object[]] $Files, [string] $FeatureRoot) {
    $count = 0
    $importPattern = '\bfrom\s*[\"''](?<specifier>(?:\.\.?/|@/|~/)[^\"'']+)[\"'']|\bimport\s*\(\s*[\"''](?<dynamicSpecifier>(?:\.\.?/|@/|~/)[^\"'']+)[\"'']|\bimport\s*[\"''](?<sideEffectSpecifier>(?:\.\.?/|@/|~/)[^\"'']+)[\"'']'

    foreach ($file in @($Files)) {
        $sourceFeature = Get-FeatureName $file.FullName $FeatureRoot
        if ([string]::IsNullOrWhiteSpace($sourceFeature)) { continue }

        $source = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($match in [regex]::Matches($source, $importPattern)) {
            $specifier = $match.Groups['specifier'].Value
            if ([string]::IsNullOrWhiteSpace($specifier)) { $specifier = $match.Groups['dynamicSpecifier'].Value }
            if ([string]::IsNullOrWhiteSpace($specifier)) { $specifier = $match.Groups['sideEffectSpecifier'].Value }
            $targetFeature = Get-ImportedFeatureName $specifier $file.FullName $FeatureRoot
            if (-not [string]::IsNullOrWhiteSpace($targetFeature) -and
                -not $targetFeature.Equals($sourceFeature, [System.StringComparison]::OrdinalIgnoreCase)) {
                $count++
            }
        }
    }

    return $count
}

function Get-FixedNavigationEntryCount([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 0 }
    $source = [System.IO.File]::ReadAllText($Path)
    $groupsMatch = [regex]::Match($source, '(?s)\bgroups\s*:\s*\[(?<groups>.*?)]\s*,?\s*\brouteParents\s*:')
    if (-not $groupsMatch.Success) { return 0 }

    $count = 0
    $entryPattern = '(?s)\{(?:(?!\{|\}).)*\}'
    foreach ($childrenMatch in [regex]::Matches($groupsMatch.Groups['groups'].Value, '(?s)\bchildren\s*:\s*\[(?<children>.*?)]')) {
        # Section tabs opt out of the fixed-entry count with primary:false.
        foreach ($entryMatch in [regex]::Matches($childrenMatch.Groups['children'].Value, $entryPattern)) {
            $entry = $entryMatch.Value
            if ($entry -match '\bid\s*:\s*[\"'']' -and $entry -notmatch '(?i)\bprimary\s*:\s*false\b') {
                $count++
            }
        }
    }
    return $count
}

function Get-DocumentActivityRecordCount([string] $SuperpowersRoot) {
    if (-not (Test-Path -LiteralPath $SuperpowersRoot -PathType Container)) { return 0 }

    $indexPath = Join-Path $SuperpowersRoot 'README.md'
    if (Test-Path -LiteralPath $indexPath -PathType Leaf) {
        $indexSource = [System.IO.File]::ReadAllText($indexPath)
        return @([regex]::Matches($indexSource, '(?im)^\s*(?:[-*]|\|)\s*(?:\[[ x]\]\s*)?(?:active|current|\u6D3B\u52A8|\u8FDB\u884C\u4E2D)\b')).Count
    }

    $count = 0
    foreach ($file in @(Get-ChildItem -LiteralPath $SuperpowersRoot -Filter '*.md' -File -Recurse |
        Where-Object { -not (Test-ExcludedPath $_.FullName) })) {
        $source = [System.IO.File]::ReadAllText($file.FullName)
        $frontMatter = [regex]::Match($source, '(?s)^---\s*(?<front>.*?)\s*---')
        if ($frontMatter.Success -and $frontMatter.Groups['front'].Value -match '(?im)^state:\s*(?:Current|Active)\s*$') {
            $count++
        }
    }
    return $count
}

$productionRoot = Join-Path $root 'backend\src'
$testRoot = Join-Path $root 'backend\tests'
$adminSourceRoot = Join-Path $root 'frontend\apps\admin\src'
$adminFeatureRoot = Join-Path $adminSourceRoot 'features'
$registrationRoot = Join-Path $productionRoot 'Bootstrap\LSTY.SevenDPanel\DependencyInjection\Registration'
$superpowersRoot = Join-Path $root 'docs\superpowers'
$productionFiles = @(Get-SourceFiles $productionRoot '*.cs')
$testFiles = @(Get-SourceFiles (Join-Path $testRoot 'LSTY.SevenDPanel.Tests') '*.cs')
$productProjects = @(Get-SourceFiles $productionRoot '*.csproj')
$testProjects = @(Get-SourceFiles $testRoot '*.csproj')
$adminSourceFiles = @(Get-SourceFiles $adminSourceRoot '*' | Where-Object { $_.Extension -in @('.ts', '.tsx', '.vue') })
$adminFeatureFiles = @($adminSourceFiles | Where-Object { $_.FullName.Replace('\', '/').StartsWith($adminFeatureRoot.Replace('\', '/').TrimEnd('/') + '/') })
$registrationFiles = @(Get-SourceFiles $registrationRoot '*.cs')
$factoryPath = Join-Path $productionRoot 'Bootstrap\LSTY.SevenDPanel\DependencyInjection\PanelServiceProviderFactory.cs'
$navigationPath = Join-Path $root 'frontend\apps\admin\src\app\navigation\navigationCatalog.ts'
$hostingProject = Join-Path $productionRoot 'Runtime\LSTY.SevenDPanel.Hosting\LSTY.SevenDPanel.Hosting.csproj'
$hostingReferencesApplication = 0
if (Test-Path -LiteralPath $hostingProject -PathType Leaf) {
    $hostingReferencesApplication = @((Get-Content -LiteralPath $hostingProject -Raw) | Select-String -Pattern 'ProjectReference[^>]*Application|Application[^<]*\.csproj' -AllMatches).Count
}

$adminFeatures = @()
if (Test-Path -LiteralPath $adminFeatureRoot -PathType Container) {
    $adminFeatures = @(Get-ChildItem -LiteralPath $adminFeatureRoot -Directory |
        Where-Object { -not (Test-ExcludedPath $_.FullName) -and $_.Name -notmatch '^(\.git|__tests__)$' } |
        Sort-Object Name)
}

$navigationGroups = 0
if (Test-Path -LiteralPath $navigationPath -PathType Leaf) {
    $navigationSource = [System.IO.File]::ReadAllText($navigationPath)
    $groupsSection = ($navigationSource -split '(?m)^\s*routeParents\s*:')[0]
    $navigationGroups = @([regex]::Matches($groupsSection, '(?m)\bchildren\s*:\s*\[')).Count
    if ($navigationGroups -eq 0) {
        $navigationGroups = @([regex]::Matches($groupsSection, "(?m)^\s{6}id:\s*'[^']+'")).Count
    }
}

$compositionRootCount = 0
foreach ($file in $productionFiles) {
    $compositionRootCount += @([regex]::Matches([System.IO.File]::ReadAllText($file.FullName), 'BuildServiceProvider\s*\(')).Count
}

$publicInterfaceTotalCount = 0
foreach ($file in $productionFiles) {
    $publicInterfaceTotalCount += @([regex]::Matches([System.IO.File]::ReadAllText($file.FullName), '(?m)\bpublic\s+(?:(?:sealed|abstract|partial)\s+)*interface\s+[A-Za-z_]\w*')).Count
}

$unknownCapabilityCount = 0
$maturityDocument = Join-Path $root 'docs\test.md'
if (Test-Path -LiteralPath $maturityDocument -PathType Leaf) {
    $maturitySource = [System.IO.File]::ReadAllText($maturityDocument)
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

$factoryLineCount = Get-LineCount $factoryPath
$result = [ordered]@{
    schemaVersion = 1
    measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    productionCsFileCount = $productionFiles.Count
    productionHandwrittenFilesOver500Count = Get-FileCountOverThreshold $productionFiles 500
    productionHandwrittenFilesOver800Count = Get-FileCountOverThreshold $productionFiles 800
    productionHandwrittenFilesOver1000Count = Get-FileCountOverThreshold $productionFiles 1000
    productProjectCount = $productProjects.Count
    backendTestProjectCount = $testProjects.Count
    backendTestFileCount = $testFiles.Count
    adminSourceFileCount = $adminSourceFiles.Count
    adminHandwrittenFilesOver400Count = Get-FileCountOverThreshold $adminSourceFiles 400
    adminHandwrittenFilesOver600Count = Get-FileCountOverThreshold $adminSourceFiles 600
    adminFeatureCount = $adminFeatures.Count
    firstLevelNavigationTaskCount = $navigationGroups
    fixedNavigationEntryCount = Get-FixedNavigationEntryCount $navigationPath
    bootstrapRegistrationLineCount = $factoryLineCount
    panelServiceProviderFactoryLineCount = $factoryLineCount
    registrationFileCount = $registrationFiles.Count
    compositionRootCount = $compositionRootCount
    publicInterfaceCount = $publicInterfaceTotalCount
    publicInterfaceTotalCount = $publicInterfaceTotalCount
    featureInternalCrossDomainImportCount = Get-FeatureInternalCrossDomainImportCount $adminFeatureFiles $adminFeatureRoot
    documentActivityRecordCount = Get-DocumentActivityRecordCount $superpowersRoot
    hostingApplicationProjectReferences = $hostingReferencesApplication
    unknownCapabilityCount = $unknownCapabilityCount
    newPublicInterfaceCount = $newPublicInterfaceCount
}

$result | ConvertTo-Json -Depth 5 -Compress
