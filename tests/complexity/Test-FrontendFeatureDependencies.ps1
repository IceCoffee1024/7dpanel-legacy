[CmdletBinding()]
param([string] $RepositoryRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
}
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $scriptDirectory '..\..'
}
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$featureRoot = Join-Path $root 'frontend\apps\admin\src\features'
$navigationPath = Join-Path $root 'frontend\apps\admin\src\app\navigation\navigationCatalog.ts'
$budgetPath = Join-Path $root 'tests\complexity\complexity-budget.json'

function Stop-Gate {
    param(
        [string] $Category,
        [string] $Message
    )

    Write-Error ("[{0}] {1}" -f $Category, $Message) -ErrorAction Continue
    exit 1
}

function Test-ExcludedPath {
    param([string] $Path)

    $normalized = $Path.Replace('/', '\')
    foreach ($segment in @($normalized -split '\\')) {
        if (@('bin', 'obj', 'node_modules', '.pnpm-store', 'generated', 'dist', 'build', 'coverage', 'snapshot', 'snapshots') -contains $segment) {
            return $true
        }
    }
    return ([System.IO.Path]::GetFileName($Path) -match '(?i)(?:\.generated\.|\.gen\.|\.snapshot\.|\.snap$)')
}

function Get-FeatureName {
    param([string] $Path)

    $normalizedPath = [System.IO.Path]::GetFullPath($Path).Replace('\', '/')
    $normalizedRoot = [System.IO.Path]::GetFullPath($featureRoot).Replace('\', '/').TrimEnd('/')
    $prefix = $normalizedRoot + '/'
    if (-not $normalizedPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }
    return (($normalizedPath.Substring($prefix.Length)) -split '/')[0]
}

function Get-ImportedFeatureName {
    param(
        [string] $Specifier,
        [string] $ImporterPath
    )

    if ($Specifier -match '^(?:@|~)/features/([^/]+)') {
        return $Matches[1]
    }
    if ($Specifier -notmatch '^\.') {
        return $null
    }
    try {
        $targetPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ImporterPath) ($Specifier -replace '/', '\')))
        return Get-FeatureName -Path $targetPath
    }
    catch {
        return $null
    }
}

function Get-FeatureSourceFiles {
    if (-not (Test-Path -LiteralPath $featureRoot -PathType Container)) {
        Stop-Gate -Category 'FeatureRoot' -Message "Feature root does not exist: $featureRoot"
    }
    return @(Get-ChildItem -LiteralPath $featureRoot -File -Recurse |
        Where-Object { $_.Extension -in @('.ts', '.tsx', '.vue') -and -not (Test-ExcludedPath -Path $_.FullName) } |
        Sort-Object FullName)
}

function Get-FeatureDependencyAllowlist {
    if (-not (Test-Path -LiteralPath $budgetPath -PathType Leaf)) {
        Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "Budget configuration does not exist: $budgetPath"
    }

    try {
        $configuration = Get-Content -LiteralPath $budgetPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "Budget configuration is not valid JSON: $($_.Exception.Message)"
    }

    $allowlistProperty = $configuration.PSObject.Properties['featureDependencyAllowlist']
    if ($null -eq $allowlistProperty -or $null -eq $allowlistProperty.Value -or $allowlistProperty.Value -is [string]) {
        Stop-Gate -Category 'FeatureDependencyAllowlist' -Message 'featureDependencyAllowlist must be an array of rule objects.'
    }

    $rules = [System.Collections.Generic.List[object]]::new()
    foreach ($rule in @($allowlistProperty.Value)) {
        if ($null -eq $rule -or $rule -is [string] -or $rule -is [ValueType] -or $rule -is [System.Array]) {
            Stop-Gate -Category 'FeatureDependencyAllowlist' -Message 'Every featureDependencyAllowlist entry must be an object.'
        }

        $requiredFields = @('importerPattern', 'targetFeature', 'specifierPattern', 'reason', 'owner', 'reviewAfter', 'reviewCondition')
        $actualFields = @($rule.PSObject.Properties.Name)
        foreach ($field in $requiredFields) {
            if ($actualFields -notcontains $field) {
                Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "featureDependencyAllowlist entry is missing '$field'."
            }
        }
        foreach ($field in $actualFields) {
            if ($requiredFields -notcontains $field) {
                Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "featureDependencyAllowlist entry contains unknown property '$field'."
            }
        }

        foreach ($field in $requiredFields) {
            $value = $rule.PSObject.Properties[$field].Value
            if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
                Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "featureDependencyAllowlist.$field must be a non-empty string."
            }
        }

        $importerPattern = $rule.importerPattern.Replace('\', '/')
        if ($importerPattern -match '(?i)^frontend/apps/admin/src/features/[*?]') {
            Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "featureDependencyAllowlist importerPattern is too broad: $importerPattern"
        }
        if ($rule.targetFeature -match '[*?]') {
            Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "featureDependencyAllowlist targetFeature must be exact: $($rule.targetFeature)"
        }
        try {
            [System.Management.Automation.WildcardPattern]::new($importerPattern, [System.Management.Automation.WildcardOptions]::IgnoreCase) | Out-Null
            [System.Management.Automation.WildcardPattern]::new($rule.specifierPattern, [System.Management.Automation.WildcardOptions]::IgnoreCase) | Out-Null
        }
        catch {
            Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "featureDependencyAllowlist contains an invalid wildcard pattern: $($_.Exception.Message)"
        }

        [void]$rules.Add([pscustomobject]@{
            ImporterPattern = $importerPattern
            TargetFeature = [string]$rule.targetFeature
            SpecifierPattern = [string]$rule.specifierPattern
            Matched = $false
        })
    }
    return @($rules.ToArray())
}

function Get-CrossFeatureImports {
    param([object[]] $Files)

    $violations = [System.Collections.Generic.List[object]]::new()
    $importPattern = '\b(?:from\s*|import\s*\(\s*|import\s*)["''](?<specifier>(?:\.\.?/|@/|~/)[^"'']+)["'']'
    foreach ($file in @($Files)) {
        $sourceFeature = Get-FeatureName -Path $file.FullName
        if ([string]::IsNullOrWhiteSpace($sourceFeature)) { continue }
        $source = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($match in [regex]::Matches($source, $importPattern)) {
            $specifier = $match.Groups['specifier'].Value
            $targetFeature = Get-ImportedFeatureName -Specifier $specifier -ImporterPath $file.FullName
            if (-not [string]::IsNullOrWhiteSpace($targetFeature) -and
                -not $targetFeature.Equals($sourceFeature, [System.StringComparison]::OrdinalIgnoreCase)) {
            $violations.Add([pscustomobject]@{
                Importer = $file.FullName
                ImporterRelative = $file.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
                SourceFeature = $sourceFeature
                TargetFeature = $targetFeature
                Specifier = $specifier
                })
            }
        }
    }
    return @($violations)
}

function Get-NavigationGroups {
    if (-not (Test-Path -LiteralPath $navigationPath -PathType Leaf)) {
        Stop-Gate -Category 'FixedNavigationEntries' -Message "Navigation catalog does not exist: $navigationPath"
    }
    $source = [System.IO.File]::ReadAllText($navigationPath)
    $groupsMatch = [regex]::Match($source, '(?s)\bgroups\s*:\s*\[(?<groups>.*?)\]\s*,?\s*\brouteParents\s*:')
    if (-not $groupsMatch.Success) {
        Stop-Gate -Category 'FixedNavigationEntries' -Message 'Navigation catalog groups/routeParents structure is not parseable.'
    }
    $groupPattern = '(?s)\{\s*id\s*:\s*["''](?<id>[^"'']+)["''].*?\bchildren\s*:\s*\[(?<children>.*?)\]'
    $groups = [System.Collections.Generic.List[object]]::new()
    foreach ($match in [regex]::Matches($groupsMatch.Groups['groups'].Value, $groupPattern)) {
        $fixedEntryCount = 0
        $entryPattern = '(?s)\{(?<entry>[^{}]*)\}'
        foreach ($entryMatch in [regex]::Matches($match.Groups['children'].Value, $entryPattern)) {
            $entry = $entryMatch.Groups['entry'].Value
            if ($entry -match '\bid\s*:\s*["''][^"'']+["'']' -and
                $entry -notmatch '\bprimary\s*:\s*false\b') {
                $fixedEntryCount++
            }
        }
        $groups.Add([pscustomobject]@{
            Id = $match.Groups['id'].Value
            Count = $fixedEntryCount
        })
    }
    if ($groups.Count -eq 0) {
        Stop-Gate -Category 'FixedNavigationEntries' -Message 'Navigation catalog contains no parseable groups.'
    }
    return @($groups)
}

$allowlist = @(Get-FeatureDependencyAllowlist)
$files = Get-FeatureSourceFiles
$crossFeatureImports = @(Get-CrossFeatureImports -Files $files)
$allowlistHits = 0
$allowlistUnmatched = 0
if ($crossFeatureImports.Count -gt 0) {
    foreach ($violation in $crossFeatureImports) {
        $matches = @($allowlist | Where-Object {
            $violation.ImporterRelative -like $_.ImporterPattern -and
            $violation.TargetFeature -eq $_.TargetFeature -and
            $violation.Specifier -like $_.SpecifierPattern
        })
        if ($matches.Count -gt 1) {
            Stop-Gate -Category 'FeatureDependencyAllowlist' -Message "Multiple allowlist rules match $($violation.ImporterRelative) -> $($violation.TargetFeature) $($violation.Specifier)."
        }
        if ($matches.Count -eq 1) {
            $allowlistHits++
            $matches[0].Matched = $true
            continue
        }

        $allowlistUnmatched++
        Write-Error ("[FeatureInternalImport] {0} ({1}) imports {2} from Feature {3}; no allowlist rule matched." -f $violation.ImporterRelative, $violation.SourceFeature, $violation.Specifier, $violation.TargetFeature) -ErrorAction Continue
    }
    if ($allowlistUnmatched -gt 0) {
        Write-Error ("[FeatureDependencyAllowlist] allowlistHits={0}; allowlistUnmatched={1}." -f $allowlistHits, $allowlistUnmatched) -ErrorAction Continue
        exit 1
    }
}

$unusedRules = @($allowlist | Where-Object { -not $_.Matched })
if ($unusedRules.Count -gt 0) {
    Write-Error ("[FeatureDependencyAllowlist] unusedRules={0}; every allowlist rule must match a current production import." -f $unusedRules.Count) -ErrorAction Continue
    exit 1
}

foreach ($group in @(Get-NavigationGroups)) {
    if ($group.Count -gt 7) {
        Write-Error ("[FixedNavigationEntries] domain={0} count={1} maximum=7." -f $group.Id, $group.Count) -ErrorAction Continue
        exit 1
    }
}

Write-Host ("Frontend feature dependency budget passed: featureFiles={0}, crossFeatureImports={1}, allowlistHits={2}, allowlistUnmatched={3}, unusedRules=0, fixedNavigationDomains={4}." -f $files.Count, $crossFeatureImports.Count, $allowlistHits, $allowlistUnmatched, @(Get-NavigationGroups).Count)
