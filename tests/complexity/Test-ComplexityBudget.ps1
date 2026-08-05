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
$measurePath = Join-Path $scriptDirectory 'Measure-Complexity.ps1'
$budgetPath = Join-Path $root 'tests\complexity\complexity-budget.json'

$knownMetricNames = @(
    'productProjectCount',
    'productionCsFileCount',
    'productionHandwrittenFilesOver500Count',
    'productionHandwrittenFilesOver800Count',
    'productionHandwrittenFilesOver1000Count',
    'backendTestProjectCount',
    'backendTestFileCount',
    'adminSourceFileCount',
    'adminHandwrittenFilesOver400Count',
    'adminHandwrittenFilesOver600Count',
    'adminFeatureCount',
    'compositionRootCount',
    'panelServiceProviderFactoryLineCount',
    'bootstrapRegistrationLineCount',
    'registrationFileCount',
    'publicInterfaceTotalCount',
    'publicInterfaceCount',
    'featureInternalCrossDomainImportCount',
    'firstLevelNavigationTaskCount',
    'fixedNavigationEntryCount',
    'documentActivityRecordCount',
    'hostingApplicationProjectReferences',
    'unknownCapabilityCount',
    'newPublicInterfaceCount'
)
$requiredBaselineMetricNames = @($knownMetricNames)
$requiredTargetMetricNames = @(
    'productProjectCount',
    'productionHandwrittenFilesOver500Count',
    'productionHandwrittenFilesOver800Count',
    'productionHandwrittenFilesOver1000Count',
    'backendTestProjectCount',
    'adminHandwrittenFilesOver400Count',
    'adminHandwrittenFilesOver600Count',
    'compositionRootCount',
    'panelServiceProviderFactoryLineCount',
    'bootstrapRegistrationLineCount',
    'registrationFileCount',
    'publicInterfaceTotalCount',
    'publicInterfaceCount',
    'featureInternalCrossDomainImportCount',
    'firstLevelNavigationTaskCount',
    'fixedNavigationEntryCount',
    'documentActivityRecordCount',
    'hostingApplicationProjectReferences',
    'unknownCapabilityCount',
    'newPublicInterfaceCount'
)
$hardInvariantNames = @(
    'productProjectCount',
    'backendTestProjectCount',
    'compositionRootCount',
    'hostingApplicationProjectReferences',
    'unknownCapabilityCount',
    'newPublicInterfaceCount',
    'firstLevelNavigationTaskCount'
)
$ratchetMetricNames = @(
    'productionHandwrittenFilesOver500Count',
    'productionHandwrittenFilesOver800Count',
    'productionHandwrittenFilesOver1000Count',
    'adminHandwrittenFilesOver400Count',
    'adminHandwrittenFilesOver600Count',
    'panelServiceProviderFactoryLineCount',
    'bootstrapRegistrationLineCount',
    'featureInternalCrossDomainImportCount',
    'fixedNavigationEntryCount',
    'documentActivityRecordCount'
)
$minimumRatchetMetricNames = @('registrationFileCount')
$fixedTargetMetricNames = @(
    'productProjectCount',
    'backendTestProjectCount',
    'compositionRootCount',
    'hostingApplicationProjectReferences',
    'unknownCapabilityCount',
    'newPublicInterfaceCount',
    'firstLevelNavigationTaskCount'
)
$lowerIsBetterMetricNames = @(
    'productionHandwrittenFilesOver500Count',
    'productionHandwrittenFilesOver800Count',
    'productionHandwrittenFilesOver1000Count',
    'adminHandwrittenFilesOver400Count',
    'adminHandwrittenFilesOver600Count',
    'panelServiceProviderFactoryLineCount',
    'bootstrapRegistrationLineCount',
    'featureInternalCrossDomainImportCount',
    'fixedNavigationEntryCount',
    'documentActivityRecordCount',
    'compositionRootCount',
    'hostingApplicationProjectReferences',
    'unknownCapabilityCount',
    'newPublicInterfaceCount',
    'firstLevelNavigationTaskCount',
    'publicInterfaceTotalCount',
    'publicInterfaceCount'
)
$script:featureDependencyBudgetVerified = $false
$productionHotspotThresholds = @{
    productionHandwrittenFilesOver500Count = 500
    productionHandwrittenFilesOver800Count = 800
    productionHandwrittenFilesOver1000Count = 1000
}

function Stop-Gate {
    param(
        [string] $Category,
        [string] $Message
    )

    Write-Error ("[{0}] {1}" -f $Category, $Message) -ErrorAction Continue
    exit 1
}

function Require-Object {
    param(
        [object] $Value,
        [string] $Context
    )

    if ($null -eq $Value -or $Value -is [string] -or $Value -is [System.Array] -or $Value -is [ValueType]) {
        Stop-Gate -Category 'BudgetSchema' -Message "$Context must be an object."
    }
}

function Get-RequiredProperty {
    param(
        [object] $Object,
        [string] $Name,
        [string] $Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        Stop-Gate -Category 'BudgetSchema' -Message "$Context is missing required property '$Name'."
    }
    return $property.Value
}

function Assert-ExactProperties {
    param(
        [object] $Object,
        [string[]] $Required,
        [string] $Context
    )

    $actual = @($Object.PSObject.Properties.Name)
    foreach ($name in $Required) {
        if ($actual -notcontains $name) {
            Stop-Gate -Category 'BudgetSchema' -Message "$Context is missing required property '$name'."
        }
    }
    foreach ($name in $actual) {
        if ($Required -notcontains $name) {
            Stop-Gate -Category 'BudgetSchema' -Message "$Context contains unknown property '$name'."
        }
    }
}

function Get-NumericProperty {
    param(
        [object] $Object,
        [string] $Name,
        [string] $Context
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name -Context $Context
    if ($value -is [bool] -or $value -isnot [ValueType] -or $value -is [double] -and $value % 1 -ne 0) {
        Stop-Gate -Category 'BudgetSchema' -Message "$Context.$Name must be a non-negative integer."
    }
    $number = [int64] $value
    if ($number -lt 0) {
        Stop-Gate -Category 'BudgetSchema' -Message "$Context.$Name must be a non-negative integer."
    }
    return $number
}

function Assert-MetricObject {
    param(
        [object] $Object,
        [string[]] $Required,
        [string] $Context
    )

    Require-Object -Value $Object -Context $Context
    $actual = @($Object.PSObject.Properties.Name)
    foreach ($name in $Required) {
        if ($actual -notcontains $name) {
            Stop-Gate -Category 'BudgetSchema' -Message "$Context is missing required metric '$name'."
        }
    }
    foreach ($name in $actual) {
        if ($knownMetricNames -notcontains $name) {
            Stop-Gate -Category 'BudgetSchema' -Message "$Context contains unknown metric '$name'."
        }
        [void](Get-NumericProperty -Object $Object -Name $name -Context $Context)
    }
}

function Assert-TargetSchema {
    param(
        [object] $Baseline,
        [object] $Targets
    )

    foreach ($name in $requiredTargetMetricNames) {
        $baselineValue = Get-NumericProperty -Object $Baseline -Name $name -Context 'baseline'
        $targetValue = Get-NumericProperty -Object $Targets -Name $name -Context 'targets'
        if ($fixedTargetMetricNames -contains $name) {
            if ($targetValue -ne $baselineValue) {
                Stop-Gate -Category 'BudgetSchema' -Message "Fixed target for $name must remain $baselineValue; configured target is $targetValue."
            }
            continue
        }
        if ($lowerIsBetterMetricNames -contains $name -and $targetValue -gt $baselineValue) {
            Stop-Gate -Category 'BudgetSchema' -Message "Target for $name raises the final threshold from $baselineValue to $targetValue."
        }
        if ($name -eq 'registrationFileCount' -and $targetValue -lt $baselineValue) {
            Stop-Gate -Category 'BudgetSchema' -Message "Target for $name lowers the required registration module count from $baselineValue to $targetValue."
        }
    }
}

function Assert-FeatureDependencyAllowlist {
    param([object] $Allowlist)

    if ($null -eq $Allowlist) { return }
    if ($Allowlist -is [string] -or $Allowlist -isnot [System.Array]) {
        Stop-Gate -Category 'BudgetSchema' -Message 'featureDependencyAllowlist must be an array of rule objects.'
    }

    $requiredFields = @('importerPattern', 'targetFeature', 'specifierPattern', 'reason', 'owner', 'reviewAfter', 'reviewCondition')
    foreach ($rule in @($Allowlist)) {
        Require-Object -Value $rule -Context 'featureDependencyAllowlist entry'
        Assert-ExactProperties -Object $rule -Required $requiredFields -Context 'featureDependencyAllowlist entry'
        foreach ($field in $requiredFields) {
            $value = Get-RequiredProperty -Object $rule -Name $field -Context 'featureDependencyAllowlist entry'
            if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
                Stop-Gate -Category 'BudgetSchema' -Message "featureDependencyAllowlist.$field must be a non-empty string."
            }
        }

        $importerPattern = $rule.importerPattern.Replace('\', '/')
        if ($importerPattern -match '(?i)^frontend/apps/admin/src/features/[*?]') {
            Stop-Gate -Category 'BudgetSchema' -Message "featureDependencyAllowlist importerPattern is too broad: $importerPattern"
        }
        if ($rule.targetFeature -match '[*?]') {
            Stop-Gate -Category 'BudgetSchema' -Message "featureDependencyAllowlist targetFeature must be exact: $($rule.targetFeature)"
        }
        try {
            [System.Management.Automation.WildcardPattern]::new($importerPattern, [System.Management.Automation.WildcardOptions]::IgnoreCase) | Out-Null
            [System.Management.Automation.WildcardPattern]::new($rule.specifierPattern, [System.Management.Automation.WildcardOptions]::IgnoreCase) | Out-Null
        }
        catch {
            Stop-Gate -Category 'BudgetSchema' -Message "featureDependencyAllowlist contains an invalid wildcard pattern: $($_.Exception.Message)"
        }
    }
}

function Get-ExceptionMatches {
    param([string] $ExceptionPath)

    $normalized = $ExceptionPath.Replace('\', '/')
    $wildcardIndex = $normalized.IndexOfAny([char[]]@('*', '?'))
    if ($wildcardIndex -lt 0) {
        $literalPath = Join-Path $root ($normalized -replace '/', '\')
        if (Test-Path -LiteralPath $literalPath -PathType Leaf) {
            return @([System.IO.Path]::GetFullPath($literalPath))
        }
        if (Test-Path -LiteralPath $literalPath -PathType Container) {
            return @(Get-ChildItem -LiteralPath $literalPath -File -Recurse -ErrorAction SilentlyContinue |
                ForEach-Object { [System.IO.Path]::GetFullPath($_.FullName) })
        }
        return @()
    }

    $beforeWildcard = $normalized.Substring(0, $wildcardIndex)
    $separatorIndex = $beforeWildcard.LastIndexOf('/')
    $baseRelative = if ($separatorIndex -lt 0) { '' } else { $beforeWildcard.Substring(0, $separatorIndex) }
    $basePath = if ([string]::IsNullOrWhiteSpace($baseRelative)) { $root } else { Join-Path $root ($baseRelative -replace '/', '\') }
    if (-not (Test-Path -LiteralPath $basePath -PathType Container)) { return @() }

    $patternRegex = [regex]::Escape($normalized).Replace('\*', '.*').Replace('\?', '.')
    $patternRegex = '^' + $patternRegex + '$'
    $matchedPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in @(Get-ChildItem -LiteralPath $basePath -File -Recurse -ErrorAction SilentlyContinue)) {
        $relative = $candidate.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
        if ($relative -match $patternRegex) {
            [void] $matchedPaths.Add([System.IO.Path]::GetFullPath($candidate.FullName))
        }
    }
    return @($matchedPaths.ToArray())
}

function Test-MeasureExcludedPath {
    param([string] $Path)

    $normalizedPath = $Path.Replace('/', '\')
    $fileName = [System.IO.Path]::GetFileName($Path)
    $excludedDirectoryNames = @(
        'bin', 'obj', 'node_modules', '.pnpm-store', '7dtd-reference', 'generated',
        'migration', 'migrations', 'snapshot', 'snapshots', 'artifact', 'artifacts',
        'dist', 'build', 'coverage'
    )
    foreach ($directoryName in @($normalizedPath -split '\\')) {
        if ($excludedDirectoryNames -contains $directoryName) { return $true }
    }
    return ($fileName -match '(?i)(?:^generated[^.]*|\.generated\.|\.gen\.|\.snapshot\.|\.snap$|^route-map\.d\.)')
}

function Get-EffectiveMetric {
    param(
        [object] $Metrics,
        [string] $Name,
        [object[]] $Exceptions
    )

    $actual = Get-Metric -Metrics $Metrics -Name $Name
    if ($Name -eq 'featureInternalCrossDomainImportCount' -and $script:featureDependencyBudgetVerified) {
        return 0
    }
    if (-not $productionHotspotThresholds.ContainsKey($Name)) { return $actual }

    $threshold = [int] $productionHotspotThresholds[$Name]
    $seen = @{}
    $excludedCount = 0
    foreach ($exception in @($Exceptions)) {
        foreach ($match in @($exception.Matches)) {
            $fullPath = [System.IO.Path]::GetFullPath($match)
            $key = $fullPath.ToLowerInvariant()
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true
            $relative = $fullPath.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
            if (-not $relative.StartsWith('backend/src/', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
            if ([System.IO.Path]::GetExtension($fullPath) -ne '.cs') { continue }
            if (Test-MeasureExcludedPath -Path $fullPath) { continue }
            if ([System.IO.File]::ReadAllLines($fullPath).Count -gt $threshold) { $excludedCount++ }
        }
    }
    return [Math]::Max([int64] 0, [int64] $actual - $excludedCount)
}

function Assert-FrontendFeatureDependencyBudget {
    param(
        [object] $Metrics,
        [string] $Phase
    )

    if (@('Wave3', 'Wave4') -notcontains $Phase) { return }

    $checkerPath = Join-Path $scriptDirectory 'Test-FrontendFeatureDependencies.ps1'
    if (-not (Test-Path -LiteralPath $checkerPath -PathType Leaf)) {
        if ((Get-Metric -Metrics $Metrics -Name 'featureInternalCrossDomainImportCount') -gt 0) {
            Stop-Gate -Category 'FeatureInternalImport' -Message "Frontend dependency checker is required while cross-Feature imports exist: $checkerPath"
        }
        $script:featureDependencyBudgetVerified = $true
        return
    }

    $checkerOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $checkerPath -RepositoryRoot $root 2>&1)
    if ($LASTEXITCODE -ne 0) {
        Stop-Gate -Category 'FeatureInternalImport' -Message ("Frontend dependency budget failed: {0}" -f (($checkerOutput | Out-String).Trim()))
    }
    $script:featureDependencyBudgetVerified = $true
}

function Write-TrendAdvisories {
    param(
        [object] $Metrics,
        [object] $Targets,
        [string] $Phase,
        [object[]] $Exceptions
    )

    if (@('Wave2', 'Wave4') -notcontains $Phase) { return }
    $name = 'productionHandwrittenFilesOver500Count'
    $actual = Get-EffectiveMetric -Metrics $Metrics -Name $name -Exceptions $Exceptions
    $target = Get-NumericProperty -Object $Targets -Name $name -Context 'targets'
    if ($actual -gt $target) {
        Write-Warning ("[Advisory] {0}={1} exceeds phase {2} trend target {3}; this trend is non-blocking." -f $name, $actual, $Phase, $target)
    }
}

function Assert-BudgetConfiguration {
    if (-not (Test-Path -LiteralPath $budgetPath -PathType Leaf)) {
        Stop-Gate -Category 'BudgetPath' -Message "Budget configuration does not exist: $budgetPath"
    }

    try {
        $configuration = Get-Content -LiteralPath $budgetPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Stop-Gate -Category 'BudgetSchema' -Message "Budget configuration is not valid JSON: $($_.Exception.Message)"
    }

    Require-Object -Value $configuration -Context 'budget configuration'
    Assert-ExactProperties -Object $configuration -Required @('schemaVersion', 'phase', 'baseline', 'targets', 'exclusions', 'exceptions', 'featureDependencyAllowlist') -Context 'budget configuration'

    $schemaVersion = Get-NumericProperty -Object $configuration -Name 'schemaVersion' -Context 'budget configuration'
    if ($schemaVersion -ne 1) {
        Stop-Gate -Category 'BudgetSchema' -Message "Unsupported budget schemaVersion '$schemaVersion'."
    }

    $phase = Get-RequiredProperty -Object $configuration -Name 'phase' -Context 'budget configuration'
    $validPhases = @('Wave0', 'Wave1', 'Wave2', 'Wave3', 'Wave4')
    if ($phase -isnot [string] -or $validPhases -notcontains $phase) {
        Stop-Gate -Category 'BudgetSchema' -Message "phase must be one of Wave0, Wave1, Wave2, Wave3, Wave4."
    }

    $baseline = Get-RequiredProperty -Object $configuration -Name 'baseline' -Context 'budget configuration'
    $targets = Get-RequiredProperty -Object $configuration -Name 'targets' -Context 'budget configuration'
    Assert-MetricObject -Object $baseline -Required $requiredBaselineMetricNames -Context 'baseline'
    Assert-MetricObject -Object $targets -Required $requiredTargetMetricNames -Context 'targets'

    $exclusions = Get-RequiredProperty -Object $configuration -Name 'exclusions' -Context 'budget configuration'
    if ($null -ne $exclusions -and ($exclusions -is [string] -or $exclusions -isnot [System.Array])) {
        Stop-Gate -Category 'BudgetSchema' -Message 'exclusions must be an array of strings.'
    }
    foreach ($exclusion in @($exclusions)) {
        if ($exclusion -isnot [string] -or [string]::IsNullOrWhiteSpace($exclusion)) {
            Stop-Gate -Category 'BudgetSchema' -Message 'Every exclusions entry must be a non-empty string.'
        }
    }

    $exceptions = Get-RequiredProperty -Object $configuration -Name 'exceptions' -Context 'budget configuration'
    if ($null -ne $exceptions -and $exceptions -is [string]) {
        Stop-Gate -Category 'BudgetSchema' -Message 'exceptions must be an array of objects.'
    }
    $validatedExceptions = [System.Collections.Generic.List[object]]::new()
    foreach ($exception in @($exceptions)) {
        Require-Object -Value $exception -Context 'exception'
        Assert-ExactProperties -Object $exception -Required @('path', 'reason', 'owner', 'reviewAfter', 'reviewCondition') -Context 'exception'
        $exceptionPath = Get-RequiredProperty -Object $exception -Name 'path' -Context 'exception'
        foreach ($field in @('path', 'reason', 'owner', 'reviewAfter', 'reviewCondition')) {
            $fieldValue = Get-RequiredProperty -Object $exception -Name $field -Context 'exception'
            if ($fieldValue -isnot [string] -or [string]::IsNullOrWhiteSpace($fieldValue)) {
                Stop-Gate -Category 'BudgetSchema' -Message "exception.$field must be a non-empty string."
            }
        }
        $matches = @(Get-ExceptionMatches -ExceptionPath $exceptionPath)
        if ($matches.Count -eq 0) {
            Stop-Gate -Category 'BudgetExceptionPath' -Message "Exception path does not exist or match any path: $exceptionPath"
        }
        [void] $validatedExceptions.Add([pscustomobject]@{
            Path = $exceptionPath
            Matches = $matches
        })
    }

    $featureDependencyAllowlist = Get-RequiredProperty -Object $configuration -Name 'featureDependencyAllowlist' -Context 'budget configuration'
    Assert-FeatureDependencyAllowlist -Allowlist $featureDependencyAllowlist

    for ($i = 0; $i -lt $validatedExceptions.Count; $i++) {
        for ($j = 0; $j -lt $i; $j++) {
            foreach ($match in @($validatedExceptions[$i].Matches)) {
                if (@($validatedExceptions[$j].Matches) -contains $match) {
                    Stop-Gate -Category 'BudgetExceptionOverlap' -Message "Exception paths overlap: $($validatedExceptions[$j].Path) and $($validatedExceptions[$i].Path)."
                }
            }
        }
    }

    Assert-TargetSchema -Baseline $baseline -Targets $targets

    return [pscustomobject]@{
        Phase = $phase
        Baseline = $baseline
        Targets = $targets
        Exceptions = @($validatedExceptions.ToArray())
        FeatureDependencyAllowlist = @($featureDependencyAllowlist)
    }
}

function Get-Metric {
    param(
        [object] $Metrics,
        [string] $Name
    )

    $property = $Metrics.PSObject.Properties[$Name]
    if ($null -eq $property) {
        Stop-Gate -Category 'MeasurementSchema' -Message "Measurement JSON is missing metric '$Name'."
    }
    if ($property.Value -is [bool] -or $property.Value -isnot [ValueType]) {
        Stop-Gate -Category 'MeasurementSchema' -Message "Measurement metric '$Name' is not numeric."
    }
    return [int64] $property.Value
}

function Get-ProjectGraph {
    $sourceRoot = Join-Path $root 'backend\src'
    $projects = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.csproj' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|7dtd-reference)\\' })
    $graph = @{}
    foreach ($project in $projects) {
        $key = [System.IO.Path]::GetFullPath($project.FullName).ToLowerInvariant()
        $graph[$key] = @()
    }
    foreach ($project in $projects) {
        try {
            [xml] $document = Get-Content -LiteralPath $project.FullName -Raw
        }
        catch {
            Stop-Gate -Category 'ProjectDependency' -Message "Cannot parse project file $($project.FullName): $($_.Exception.Message)"
        }
        $references = @()
        foreach ($reference in @($document.SelectNodes('//*[local-name()="ProjectReference"]'))) {
            $include = [string] $reference.Include
            if ([string]::IsNullOrWhiteSpace($include)) {
                Stop-Gate -Category 'ProjectDependency' -Message "ProjectReference without Include in $($project.FullName)."
            }
            $target = [System.IO.Path]::GetFullPath((Join-Path $project.DirectoryName ($include -replace '/', '\'))).ToLowerInvariant()
            if (-not $graph.ContainsKey($target)) {
                Stop-Gate -Category 'ProjectDependency' -Message "ProjectReference target does not exist: $include from $($project.FullName)."
            }
            $references += $target
        }
        $graph[[System.IO.Path]::GetFullPath($project.FullName).ToLowerInvariant()] = @($references)
    }
    return $graph
}

function Assert-AcyclicProjectGraph {
    param([hashtable] $Graph)

    $visited = @{}
    $active = @{}
    function Visit-Project([string] $Node) {
        if ($active.ContainsKey($Node)) {
            Stop-Gate -Category 'ProjectDependency' -Message "Project dependency cycle detected at $Node."
        }
        if ($visited.ContainsKey($Node)) { return }
        $active[$Node] = $true
        foreach ($target in @($Graph[$Node])) {
            Visit-Project -Node $target
        }
        $active.Remove($Node)
        $visited[$Node] = $true
    }
    foreach ($node in @($Graph.Keys)) {
        Visit-Project -Node $node
    }
}

function Assert-RegistrationModules {
    $registrationRoot = Join-Path $root 'backend\src\Bootstrap\LSTY.SevenDPanel\DependencyInjection\Registration'
    if (-not (Test-Path -LiteralPath $registrationRoot -PathType Container)) { return }
    foreach ($file in @(Get-ChildItem -LiteralPath $registrationRoot -Filter '*.cs' -File -Recurse)) {
        $source = [System.IO.File]::ReadAllText($file.FullName)
        if ($source -match 'BuildServiceProvider\s*\(') {
            Stop-Gate -Category 'RegistrationSideEffect' -Message "Registration module calls BuildServiceProvider: $($file.FullName)."
        }
        $sideEffectRules = @(
            [pscustomobject]@{ Kind = 'Task'; Pattern = '(?i)\bTask\.Run\s*\(|\bTask\.Factory\.StartNew\s*\(' }
            [pscustomobject]@{ Kind = 'Thread'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Threading\.)?Thread\s*\(' }
            [pscustomobject]@{ Kind = 'ThreadPool'; Pattern = '(?i)\b(?:System\.Threading\.)?ThreadPool\.(?:QueueUserWorkItem|UnsafeQueueUserWorkItem)\s*\(' }
            [pscustomobject]@{ Kind = 'Timer'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Threading\.)?Timer\s*\(' }
            [pscustomobject]@{ Kind = 'FileSystemWatcher'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.IO\.)?FileSystemWatcher\s*\(' }
            [pscustomobject]@{ Kind = 'HttpClient'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Net\.Http\.)?HttpClient\s*\(' }
            [pscustomobject]@{ Kind = 'TcpClient'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Net\.Sockets\.)?TcpClient\s*\(' }
            [pscustomobject]@{ Kind = 'UdpClient'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Net\.Sockets\.)?UdpClient\s*\(' }
            [pscustomobject]@{ Kind = 'Socket'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Net\.Sockets\.)?Socket\s*\(' }
            [pscustomobject]@{ Kind = 'NetworkStream'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Net\.Sockets\.)?NetworkStream\s*\(' }
            [pscustomobject]@{ Kind = 'WebClient'; Pattern = '(?i)\bnew\s+(?:global::)?(?:System\.Net\.)?WebClient\s*\(' }
        )
        foreach ($rule in $sideEffectRules) {
            if ($source -match $rule.Pattern) {
                Stop-Gate -Category 'RegistrationSideEffect' -Message "Registration module has prohibited $($rule.Kind) side effect: $($file.FullName)."
            }
        }
    }
}

function Get-NavigationGroups {
    $path = Join-Path $root 'frontend\apps\admin\src\app\navigation\navigationCatalog.ts'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Stop-Gate -Category 'FixedNavigationEntries' -Message "Navigation catalog does not exist: $path"
    }
    $source = [System.IO.File]::ReadAllText($path)
    $groupsMatch = [regex]::Match($source, '(?s)\bgroups\s*:\s*\[(?<groups>.*?)\]\s*,?\s*\brouteParents\s*:')
    if (-not $groupsMatch.Success) {
        Stop-Gate -Category 'FixedNavigationEntries' -Message 'Navigation catalog groups/routeParents structure is not parseable.'
    }
    $groups = @()
    $groupPattern = '(?s)\{\s*id\s*:\s*["''](?<id>[^"'']+)["''].*?\bchildren\s*:\s*\[(?<children>.*?)\]'
    foreach ($match in [regex]::Matches($groupsMatch.Groups['groups'].Value, $groupPattern)) {
        $fixedEntryCount = 0
        foreach ($entryMatch in [regex]::Matches($match.Groups['children'].Value, '(?s)\{(?<entry>[^{}]*)\}')) {
            $entry = $entryMatch.Groups['entry'].Value
            if ($entry -match '\bid\s*:\s*["''][^"'']+["'']' -and
                $entry -notmatch '(?i)\bprimary\s*:\s*false\b') {
                $fixedEntryCount++
            }
        }
        $groups += [pscustomobject]@{
            Id = $match.Groups['id'].Value
            Count = $fixedEntryCount
        }
    }
    if ($groups.Count -eq 0) {
        Stop-Gate -Category 'FixedNavigationEntries' -Message 'Navigation catalog contains no parseable groups.'
    }
    return $groups
}

function Assert-FixedNavigationEntries {
    param([string] $Phase)

    if (@('Wave3', 'Wave4') -notcontains $Phase) { return }
    foreach ($group in @(Get-NavigationGroups)) {
        if ($group.Count -gt 7) {
            Stop-Gate -Category 'FixedNavigationEntries' -Message "Navigation domain '$($group.Id)' has $($group.Count) fixed entries; maximum is 7."
        }
    }
}

function Assert-HardMetrics {
    param(
        [object] $Metrics,
        [object] $Baseline
    )

    foreach ($name in $hardInvariantNames) {
        $actual = Get-Metric -Metrics $Metrics -Name $name
        $expected = Get-NumericProperty -Object $Baseline -Name $name -Context 'baseline'
        if ($name -eq 'productProjectCount' -and $actual -ne 8) {
            Stop-Gate -Category 'HardInvariant' -Message "Eight product projects required; actual=$actual."
        }
        if ($name -eq 'backendTestProjectCount' -and $actual -ne 1) {
            Stop-Gate -Category 'HardInvariant' -Message "One backend test project required; actual=$actual."
        }
        if ($name -eq 'compositionRootCount' -and $actual -ne 1) {
            Stop-Gate -Category 'ProviderInvariant' -Message "Exactly one composition root/Provider is required; actual=$actual."
        }
        if ($name -eq 'hostingApplicationProjectReferences' -and $actual -ne 0) {
            Stop-Gate -Category 'HostingDependency' -Message "Hosting -> Application references must be zero; actual=$actual."
        }
        if ($name -eq 'unknownCapabilityCount' -and $actual -ne 0) {
            Stop-Gate -Category 'UnknownCapability' -Message "Unknown Capability count must be zero; actual=$actual."
        }
        if ($name -eq 'newPublicInterfaceCount' -and $actual -ne 0) {
            Stop-Gate -Category 'PublicInterface' -Message "New public interface count must be zero; actual=$actual."
        }
        if ($name -eq 'firstLevelNavigationTaskCount' -and $actual -ne 6) {
            Stop-Gate -Category 'NavigationDomain' -Message "Exactly six first-level navigation domains are required; actual=$actual."
        }
        if ($name -eq 'publicInterfaceTotalCount') { continue }
    }

    $actualPublicInterfaces = Get-Metric -Metrics $Metrics -Name 'publicInterfaceTotalCount'
    $baselinePublicInterfaces = Get-NumericProperty -Object $Baseline -Name 'publicInterfaceTotalCount' -Context 'baseline'
    if ($actualPublicInterfaces -gt $baselinePublicInterfaces) {
        Stop-Gate -Category 'PublicInterface' -Message "Public interface total increased from $baselinePublicInterfaces to $actualPublicInterfaces without a documented contract."
    }
}

function Assert-Ratchet {
    param(
        [object] $Metrics,
        [object] $Baseline
    )

    foreach ($name in $ratchetMetricNames) {
        $actual = Get-Metric -Metrics $Metrics -Name $name
        $previous = Get-NumericProperty -Object $Baseline -Name $name -Context 'baseline'
        if ($actual -gt $previous) {
            Stop-Gate -Category 'HotspotRatchet' -Message "$name worsened from $previous to $actual."
        }
    }
    foreach ($name in $minimumRatchetMetricNames) {
        $actual = Get-Metric -Metrics $Metrics -Name $name
        $previous = Get-NumericProperty -Object $Baseline -Name $name -Context 'baseline'
        if ($actual -lt $previous) {
            Stop-Gate -Category 'HotspotRatchet' -Message "$name regressed from $previous to $actual."
        }
    }
}

function Get-ActiveTargetNames {
    param([string] $Phase)

    switch ($Phase) {
        'Wave0' { return @() }
        'Wave1' { return @('panelServiceProviderFactoryLineCount', 'bootstrapRegistrationLineCount', 'registrationFileCount') }
        'Wave2' { return @('productionHandwrittenFilesOver800Count', 'productionHandwrittenFilesOver1000Count') }
        'Wave3' { return @('adminHandwrittenFilesOver400Count', 'adminHandwrittenFilesOver600Count', 'featureInternalCrossDomainImportCount', 'fixedNavigationEntryCount') }
        'Wave4' { return @($requiredTargetMetricNames | Where-Object { $_ -ne 'productionHandwrittenFilesOver500Count' }) }
    }
    return @()
}

function Assert-TargetThresholds {
    param(
        [object] $Metrics,
        [object] $Baseline,
        [object] $Targets,
        [string] $Phase,
        [object[]] $Exceptions
    )

    foreach ($name in @(Get-ActiveTargetNames -Phase $Phase)) {
        $actual = Get-EffectiveMetric -Metrics $Metrics -Name $name -Exceptions $Exceptions
        $target = Get-NumericProperty -Object $Targets -Name $name -Context 'targets'
        $baselineValue = Get-NumericProperty -Object $Baseline -Name $name -Context 'baseline'
        if ($lowerIsBetterMetricNames -contains $name) {
            if ($target -gt $baselineValue) {
                Stop-Gate -Category 'BudgetSchema' -Message "Target for $name raises the final threshold from $baselineValue to $target."
            }
            if ($actual -gt $target) {
                Stop-Gate -Category 'HotspotTarget' -Message "$name=$actual exceeds phase $Phase target $target."
            }
        }
        elseif ($fixedTargetMetricNames -contains $name) {
            if ($target -ne $baselineValue) {
                Stop-Gate -Category 'BudgetSchema' -Message "Fixed target for $name must remain $baselineValue; configured target is $target."
            }
        }
        elseif ($name -eq 'registrationFileCount') {
            if ($target -lt $baselineValue) {
                Stop-Gate -Category 'BudgetSchema' -Message "Target for $name lowers the required registration module count from $baselineValue to $target."
            }
            if ($actual -lt $target) {
                Stop-Gate -Category 'HotspotTarget' -Message "$name=$actual is below phase $Phase target $target."
            }
        }
    }
}

$configuration = Assert-BudgetConfiguration
try {
    $measureOutput = @(& $measurePath -RepositoryRoot $root)
}
catch {
    Stop-Gate -Category 'Measurement' -Message "Measure-Complexity.ps1 failed: $($_.Exception.Message)"
}
try {
    $metrics = $measureOutput | ConvertFrom-Json -ErrorAction Stop
}
catch {
    Stop-Gate -Category 'MeasurementSchema' -Message "Measure-Complexity.ps1 did not return valid JSON: $($_.Exception.Message)"
}

Assert-RegistrationModules
$projectGraph = Get-ProjectGraph
Assert-AcyclicProjectGraph -Graph $projectGraph
Assert-HardMetrics -Metrics $metrics -Baseline $configuration.Baseline
Assert-FixedNavigationEntries -Phase $configuration.Phase
Assert-FrontendFeatureDependencyBudget -Metrics $metrics -Phase $configuration.Phase
Assert-Ratchet -Metrics $metrics -Baseline $configuration.Baseline
Write-TrendAdvisories -Metrics $metrics -Targets $configuration.Targets -Phase $configuration.Phase -Exceptions $configuration.Exceptions
Assert-TargetThresholds -Metrics $metrics -Baseline $configuration.Baseline -Targets $configuration.Targets -Phase $configuration.Phase -Exceptions $configuration.Exceptions

Write-Host ("Complexity budget passed: phase={0}, projects={1}, tests={2}, compositionRoots={3}, navigationTasks={4}." -f $configuration.Phase, (Get-Metric -Metrics $metrics -Name 'productProjectCount'), (Get-Metric -Metrics $metrics -Name 'backendTestProjectCount'), (Get-Metric -Metrics $metrics -Name 'compositionRootCount'), (Get-Metric -Metrics $metrics -Name 'firstLevelNavigationTaskCount'))
