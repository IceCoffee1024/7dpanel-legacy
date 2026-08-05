[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$budgetScript = Join-Path $PSScriptRoot '..\Test-ComplexityBudget.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('seven-dpanel-budget-' + [guid]::NewGuid().ToString('N'))
$failures = [System.Collections.Generic.List[string]]::new()

function Write-FixtureFile {
    param(
        [string] $Root,
        [string] $RelativePath,
        [string] $Content
    )

    $path = Join-Path $Root $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $Content -NoNewline -Encoding UTF8
}

function New-HotspotSource {
    param(
        [string] $TypeName,
        [int] $Padding = 1000
    )

    return ((@("public sealed class $TypeName {") + @(1..$Padding | ForEach-Object { "    private int value$_;" }) + @('}')) -join "`n")
}

function New-BaselineFixture {
    param([string] $Root)

    New-Item -ItemType Directory -Path $Root -Force | Out-Null

    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/LSTY.SevenDPanel.Domain/LSTY.SevenDPanel.Domain.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/LSTY.SevenDPanel.Application/LSTY.SevenDPanel.Application.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Runtime/LSTY.SevenDPanel.Hosting/LSTY.SevenDPanel.Hosting.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/FixtureA/FixtureA.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/FixtureB/FixtureB.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/FixtureC/FixtureC.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/FixtureD/FixtureD.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/FixtureE/FixtureE.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj' -Content '<Project />'
    Write-FixtureFile -Root $Root -RelativePath 'backend/tests/LSTY.SevenDPanel.Tests/MarkerTests.cs' -Content 'public sealed class MarkerTests { }'
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs' -Content @'
public sealed class PanelServiceProviderFactory
{
    public object Create()
    {
        return services.BuildServiceProvider();
    }
}
'@
    Write-FixtureFile -Root $Root -RelativePath 'backend/src/Bootstrap/LSTY.SevenDPanel/Marker.cs' -Content 'public sealed class Marker { }'
    Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/overview/index.ts' -Content 'export {}'
    Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/app/navigation/navigationCatalog.ts' -Content @'
export const navigationCatalog = {
  groups: [
    {
      id: 'overview',
      children: [{ id: 'overview-home', routeName: '/' }],
    },
    {
      id: 'operations',
      children: [{ id: 'operations-home', routeName: '/operations' }],
    },
    {
      id: 'players',
      children: [{ id: 'players-home', routeName: '/players' }],
    },
    {
      id: 'community',
      children: [{ id: 'community-home', routeName: '/community' }],
    },
    {
      id: 'economy',
      children: [{ id: 'economy-home', routeName: '/economy' }],
    },
    {
      id: 'system',
      children: [{ id: 'system-home', routeName: '/system' }],
    },
  ],
  routeParents: [],
}
'@
    Write-FixtureFile -Root $Root -RelativePath 'docs/test.md' -Content @'
<!-- CAPABILITY_MATURITY_START -->
| ID | Owner | Contract | Anchor | Boundaries | Evidence | Maturity | Gate | Blockers/expiry |
|---|---|---|---|---|---|---|---|---|
| CAP-01 | Platform | contract | anchor | boundary | evidence | ready | gate | none |
<!-- CAPABILITY_MATURITY_END -->
'@
    Write-FixtureFile -Root $Root -RelativePath 'tests/complexity/complexity-budget.json' -Content @'
{
  "schemaVersion": 1,
  "phase": "Wave0",
  "baseline": {
    "productProjectCount": 8,
    "productionCsFileCount": 2,
    "productionHandwrittenFilesOver500Count": 0,
    "productionHandwrittenFilesOver800Count": 0,
    "productionHandwrittenFilesOver1000Count": 0,
    "backendTestProjectCount": 1,
    "backendTestFileCount": 1,
    "adminSourceFileCount": 2,
    "adminHandwrittenFilesOver400Count": 0,
    "adminHandwrittenFilesOver600Count": 0,
    "adminFeatureCount": 1,
    "compositionRootCount": 1,
    "panelServiceProviderFactoryLineCount": 7,
    "bootstrapRegistrationLineCount": 7,
    "registrationFileCount": 0,
    "publicInterfaceTotalCount": 0,
    "publicInterfaceCount": 0,
    "featureInternalCrossDomainImportCount": 0,
    "fixedNavigationEntryCount": 6,
    "documentActivityRecordCount": 0,
    "hostingApplicationProjectReferences": 0,
    "unknownCapabilityCount": 0,
    "newPublicInterfaceCount": 0,
    "firstLevelNavigationTaskCount": 6
  },
  "targets": {
    "productProjectCount": 8,
    "compositionRootCount": 1,
    "productionCsFileCount": 2,
    "productionHandwrittenFilesOver500Count": 0,
    "productionHandwrittenFilesOver800Count": 0,
    "productionHandwrittenFilesOver1000Count": 0,
    "backendTestProjectCount": 1,
    "backendTestFileCount": 1,
    "adminSourceFileCount": 2,
    "adminHandwrittenFilesOver400Count": 0,
    "adminHandwrittenFilesOver600Count": 0,
    "adminFeatureCount": 1,
    "panelServiceProviderFactoryLineCount": 7,
    "bootstrapRegistrationLineCount": 7,
    "registrationFileCount": 7,
    "publicInterfaceTotalCount": 0,
    "publicInterfaceCount": 0,
    "featureInternalCrossDomainImportCount": 0,
    "fixedNavigationEntryCount": 6,
    "documentActivityRecordCount": 0,
    "hostingApplicationProjectReferences": 0,
    "unknownCapabilityCount": 0,
    "newPublicInterfaceCount": 0,
    "firstLevelNavigationTaskCount": 6
  },
  "exclusions": [],
  "exceptions": [],
  "featureDependencyAllowlist": []
}
'@
}

function Invoke-Budget {
    param([string] $Root)

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $budgetScript -RepositoryRoot $Root 2>&1)
        return [pscustomobject]@{
            ExitCode = [int] $LASTEXITCODE
            Output = ($output | Out-String)
        }
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

function Invoke-Scenario {
    param(
        [string] $Name,
        [int] $ExpectedExitCode,
        [string] $ExpectedCategory,
        [string] $ExpectedDetail,
        [string] $ExpectedAdvisory,
        [scriptblock] $Mutate
    )

    $root = Join-Path $fixtureRoot $Name
    New-BaselineFixture -Root $root
    & $Mutate $root
    $result = Invoke-Budget -Root $root
    $actualExitCode = $result.ExitCode
    Write-Host ("{0}: actual={1}, expected={2}" -f $Name, $actualExitCode, $ExpectedExitCode)
    if ($actualExitCode -ne $ExpectedExitCode) {
        $failures.Add("$Name returned exit code $actualExitCode; expected $ExpectedExitCode. Output: $($result.Output.Trim())")
    }
    elseif ($ExpectedExitCode -ne 0 -and $result.Output -notmatch [regex]::Escape("[$ExpectedCategory]")) {
        $failures.Add("$Name returned the expected exit code but did not report [$ExpectedCategory]. Output: $($result.Output.Trim())")
    }
    elseif ($ExpectedExitCode -ne 0 -and -not [string]::IsNullOrWhiteSpace($ExpectedDetail) -and $result.Output -notmatch [regex]::Escape($ExpectedDetail)) {
        $failures.Add("$Name returned the expected exit code and category but did not report '$ExpectedDetail'. Output: $($result.Output.Trim())")
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedAdvisory) -and $result.Output -notmatch [regex]::Escape($ExpectedAdvisory)) {
        $failures.Add("$Name did not report advisory '$ExpectedAdvisory'. Output: $($result.Output.Trim())")
    }
}

try {
    New-BaselineFixture -Root (Join-Path $fixtureRoot 'valid')
    $validResult = Invoke-Budget -Root (Join-Path $fixtureRoot 'valid')
    $validExitCode = $validResult.ExitCode
    Write-Host ("valid-baseline: actual={0}, expected=0" -f $validExitCode)
    if ($validExitCode -ne 0) {
        $failures.Add("valid baseline returned exit code $validExitCode; expected 0. Output: $($validResult.Output.Trim())")
    }

    Invoke-Scenario -Name 'second-provider' -ExpectedExitCode 1 -ExpectedCategory 'ProviderInvariant' -Mutate {
        param([string] $Root)
        Add-Content -LiteralPath (Join-Path $Root 'backend/src/Bootstrap/LSTY.SevenDPanel/Marker.cs') -Value 'services.BuildServiceProvider();'
    }

    Invoke-Scenario -Name 'project-dependency-cycle' -ExpectedExitCode 1 -ExpectedCategory 'ProjectDependency' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/CycleA/CycleA.csproj' -Content '<Project><ItemGroup><ProjectReference Include="..\CycleB\CycleB.csproj" /></ItemGroup></Project>'
        Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/CycleB/CycleB.csproj' -Content '<Project><ItemGroup><ProjectReference Include="..\CycleA\CycleA.csproj" /></ItemGroup></Project>'
    }

    Invoke-Scenario -Name 'hosting-to-application' -ExpectedExitCode 1 -ExpectedCategory 'HostingDependency' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'backend/src/Runtime/LSTY.SevenDPanel.Hosting/LSTY.SevenDPanel.Hosting.csproj' -Content '<Project><ItemGroup><ProjectReference Include="..\..\Core\LSTY.SevenDPanel.Application\LSTY.SevenDPanel.Application.csproj" /></ItemGroup></Project>'
    }

    Invoke-Scenario -Name 'unknown-capability' -ExpectedExitCode 1 -ExpectedCategory 'UnknownCapability' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'docs/test.md' -Content @'
<!-- CAPABILITY_MATURITY_START -->
| ID | Owner | Contract | Anchor | Boundaries | Evidence | Maturity | Gate | Blockers/expiry |
|---|---|---|---|---|---|---|---|---|
| CAP-01 | UnknownCapability | contract | anchor | boundary | evidence | ready | gate | none |
<!-- CAPABILITY_MATURITY_END -->
'@
    }

    Invoke-Scenario -Name 'undocumented-public-interface' -ExpectedExitCode 1 -ExpectedCategory 'PublicInterface' -Mutate {
        param([string] $Root)
        Add-Content -LiteralPath (Join-Path $Root 'backend/src/Bootstrap/LSTY.SevenDPanel/Marker.cs') -Value 'public interface IUndocumentedContract { }'
    }

    Invoke-Scenario -Name 'registration-build-provider' -ExpectedExitCode 1 -ExpectedCategory 'RegistrationSideEffect' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlatformServiceRegistration.cs' -Content @'
public static class PlatformServiceRegistration
{
    public static void Register(IServiceCollection services)
    {
        services.BuildServiceProvider();
    }
}
'@
    }

    Invoke-Scenario -Name 'registration-background-startup' -ExpectedExitCode 1 -ExpectedCategory 'RegistrationSideEffect' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlatformServiceRegistration.cs' -Content @'
public static class PlatformServiceRegistration
{
    public static void Register(IServiceCollection services)
    {
        Task.Run(() => StartBackgroundWork());
    }
}
'@
    }

    Invoke-Scenario -Name 'registration-network-client' -ExpectedExitCode 1 -ExpectedCategory 'RegistrationSideEffect' -ExpectedDetail 'HttpClient' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlatformServiceRegistration.cs' -Content @'
public static class PlatformServiceRegistration
{
    public static void Register(IServiceCollection services)
    {
        var client = new System.Net.Http.HttpClient();
    }
}
'@
    }

    Invoke-Scenario -Name 'more-than-seven-fixed-entries' -ExpectedExitCode 1 -ExpectedCategory 'FixedNavigationEntries' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/app/navigation/navigationCatalog.ts' -Content @'
export const navigationCatalog = {
  groups: [
    {
      id: 'operations',
      children: [
        { id: 'one' },
        { id: 'two' },
        { id: 'three' },
        { id: 'four' },
        { id: 'five' },
        { id: 'six' },
        { id: 'seven' },
        { id: 'eight' },
      ],
    },
    {
      id: 'overview',
      children: [{ id: 'overview-home' }],
    },
    {
      id: 'players',
      children: [{ id: 'players-home' }],
    },
    {
      id: 'community',
      children: [{ id: 'community-home' }],
    },
    {
      id: 'economy',
      children: [{ id: 'economy-home' }],
    },
    {
      id: 'system',
      children: [{ id: 'system-home' }],
    },
  ],
  routeParents: [],
}
'@
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.phase = 'Wave3'
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    Invoke-Scenario -Name 'wave2-valid-file-exception' -ExpectedExitCode 0 -ExpectedCategory '' -Mutate {
        param([string] $Root)
        $relativePath = 'backend/src/Core/ExceptionHotspot.cs'
        Write-FixtureFile -Root $Root -RelativePath $relativePath -Content (New-HotspotSource -TypeName 'ExceptionHotspot')
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.phase = 'Wave2'
        foreach ($name in @('productionHandwrittenFilesOver500Count', 'productionHandwrittenFilesOver800Count', 'productionHandwrittenFilesOver1000Count')) {
            $config.baseline.$name = 1
        }
        $config.exceptions = @([pscustomobject]@{
            path = $relativePath
            reason = 'Fixture hotspot is explicitly retained for the approved exception path test.'
            owner = 'agent-2'
            reviewAfter = 'Task 8 review'
            reviewCondition = 'Remove after the retained hotspot is split and recount the Wave 2 budget.'
        })
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    Invoke-Scenario -Name 'wave2-over-500-advisory' -ExpectedExitCode 0 -ExpectedCategory '' -ExpectedAdvisory '[Advisory] productionHandwrittenFilesOver500Count' -Mutate {
        param([string] $Root)
        $relativePath = 'backend/src/Core/TrendHotspot.cs'
        Write-FixtureFile -Root $Root -RelativePath $relativePath -Content (New-HotspotSource -TypeName 'TrendHotspot' -Padding 500)
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.phase = 'Wave2'
        $config.baseline.productionHandwrittenFilesOver500Count = 1
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    Invoke-Scenario -Name 'wave2-non-exception-hotspot-still-fails' -ExpectedExitCode 1 -ExpectedCategory 'HotspotTarget' -Mutate {
        param([string] $Root)
        $exceptionPath = 'backend/src/Core/ExceptionHotspot.cs'
        Write-FixtureFile -Root $Root -RelativePath $exceptionPath -Content (New-HotspotSource -TypeName 'ExceptionHotspot')
        Write-FixtureFile -Root $Root -RelativePath 'backend/src/Core/UnreviewedHotspot.cs' -Content (New-HotspotSource -TypeName 'UnreviewedHotspot')
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.phase = 'Wave2'
        foreach ($name in @('productionHandwrittenFilesOver500Count', 'productionHandwrittenFilesOver800Count', 'productionHandwrittenFilesOver1000Count')) {
            $config.baseline.$name = 2
        }
        $config.exceptions = @([pscustomobject]@{
            path = $exceptionPath
            reason = 'Fixture hotspot is explicitly retained for the approved exception path test.'
            owner = 'agent-2'
            reviewAfter = 'Task 8 review'
            reviewCondition = 'Remove after the retained hotspot is split and recount the Wave 2 budget.'
        })
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    Invoke-Scenario -Name 'budget-config-overlapping-exceptions' -ExpectedExitCode 1 -ExpectedCategory 'BudgetExceptionOverlap' -Mutate {
        param([string] $Root)
        $exactPath = 'backend/src/Bootstrap/LSTY.SevenDPanel/Marker.cs'
        $wildcardPath = 'backend/src/Bootstrap/LSTY.SevenDPanel/*.cs'
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.exceptions = @(
            [pscustomobject]@{
                path = $exactPath
                reason = 'Fixture overlap test.'
                owner = 'agent-2'
                reviewAfter = 'Task 8 review'
                reviewCondition = 'Remove the duplicate path before approval.'
            }
            [pscustomobject]@{
                path = $wildcardPath
                reason = 'Fixture overlap test.'
                owner = 'agent-2'
                reviewAfter = 'Task 8 review'
                reviewCondition = 'Remove the overlapping wildcard before approval.'
            }
        )
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    Invoke-Scenario -Name 'budget-config-missing-field' -ExpectedExitCode 1 -ExpectedCategory 'BudgetSchema' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'tests/complexity/complexity-budget.json' -Content '{ "schemaVersion": 1, "phase": "Wave0", "baseline": {}, "targets": {} }'
    }

    Invoke-Scenario -Name 'wave2-defers-feature-import-target' -ExpectedExitCode 0 -ExpectedCategory '' -Mutate {
        param([string] $Root)
        Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/alpha/model/useAlpha.ts' -Content "import { betaModel } from '../../beta/model/beta'`nexport { betaModel }"
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.phase = 'Wave2'
        $config.baseline.featureInternalCrossDomainImportCount = 1
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    Invoke-Scenario -Name 'budget-config-raised-public-interface-target' -ExpectedExitCode 1 -ExpectedCategory 'BudgetSchema' -Mutate {
        param([string] $Root)
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.phase = 'Wave4'
        $config.targets.publicInterfaceTotalCount = 999
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
    }

    Invoke-Scenario -Name 'budget-config-path-missing' -ExpectedExitCode 1 -ExpectedCategory 'BudgetExceptionPath' -Mutate {
        param([string] $Root)
        $configPath = Join-Path $Root 'tests/complexity/complexity-budget.json'
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $config.exceptions = @(
            [pscustomobject]@{
            path = 'backend/src/does-not-exist.cs'
            reason = 'fixture'
            owner = 'agent-2'
            reviewAfter = '2026-09-01'
            reviewCondition = 'Must remain invalid for this RED fixture.'
            }
            [pscustomobject]@{
                path = 'backend/src/also-does-not-exist.cs'
                reason = 'fixture'
                owner = 'agent-2'
                reviewAfter = '2026-09-01'
                reviewCondition = 'Must remain invalid for this RED fixture.'
            }
        )
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
        $unused = @'
{
  "schemaVersion": 1,
  "phase": "Wave0",
  "baseline": {
    "productProjectCount": 8,
    "backendTestProjectCount": 1,
    "compositionRootCount": 1,
    "hostingApplicationProjectReferences": 0,
    "unknownCapabilityCount": 0,
    "newPublicInterfaceCount": 0,
    "firstLevelNavigationTaskCount": 6,
    "maxFixedEntriesPerDomain": 7
  },
  "targets": {
    "compositionRootCount": 1,
    "hostingApplicationProjectReferences": 0,
    "unknownCapabilityCount": 0,
    "newPublicInterfaceCount": 0,
    "firstLevelNavigationTaskCount": 6,
    "maxFixedEntriesPerDomain": 7
  },
  "exceptions": [
    { "path": "backend/src/does-not-exist.cs", "reason": "fixture", "owner": "agent-2", "reviewAfter": "2026-09-01" }
  ]
}
'@
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw 'Complexity budget RED fixture assertions failed.'
}

Write-Host 'Complexity budget fail-closed fixtures passed.'
