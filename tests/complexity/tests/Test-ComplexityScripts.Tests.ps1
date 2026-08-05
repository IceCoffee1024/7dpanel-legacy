$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$complexityRoot = Split-Path $PSScriptRoot -Parent
$measurePath = Join-Path $complexityRoot 'Measure-Complexity.ps1'
$budgetPath = Join-Path $complexityRoot 'Test-ComplexityBudget.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-complexity-' + [Guid]::NewGuid().ToString('N'))

function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Invoke-Json([string] $Path, [string] $Root) { return (& $Path -RepositoryRoot $Root | ConvertFrom-Json) }
function Write-FixtureFile {
    param(
        [string] $Root,
        [string] $RelativePath,
        [string[]] $Content
    )

    $path = Join-Path $Root $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $Content -Encoding UTF8
}
function New-LongFixture([int] $Padding, [string] $Prefix) {
    return @(
        "$Prefix {"
        @(1..$Padding | ForEach-Object { "    int value$_;" })
        '}'
    )
}
function New-ExactLineFixture([int] $LineCount, [string] $Prefix) {
    return @(1..$LineCount | ForEach-Object { "$Prefix line$_" })
}

try {
    $paths = @(
        'backend/src/Runtime/LSTY.SevenDPanel.Hosting/LSTY.SevenDPanel.Hosting.csproj',
        'backend/src/Core/LSTY.SevenDPanel.Application/LSTY.SevenDPanel.Application.csproj',
        'backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs',
        'backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj',
        'frontend/apps/admin/src/app/navigation/navigationCatalog.ts',
        'docs/test.md'
    )
    foreach ($path in $paths) {
        $full = Join-Path $temporaryRoot $path
        New-Item -ItemType Directory -Path (Split-Path $full -Parent) -Force | Out-Null
        if ($path -like '*.csproj') { Set-Content -LiteralPath $full -Value '<Project><ItemGroup><ProjectReference Include="..\Core\LSTY.SevenDPanel.Application\LSTY.SevenDPanel.Application.csproj" /></ItemGroup></Project>' -Encoding UTF8 }
        elseif ($path -like '*.cs') { Set-Content -LiteralPath $full -Value 'public sealed class Root { }' -Encoding UTF8 }
        elseif ($path -like '*.ts') { Set-Content -LiteralPath $full -Value @'
export const navigationCatalog = {
  groups: [
    { id: 'overview', children: [{ id: 'overview-home' }] },
    { id: 'operations', children: [{ id: 'operations-home' }, { id: 'operations-automation', primary: false, sectionId: 'operations-automation' }] },
    { id: 'players', children: [{ id: 'players-home' }] },
    { id: 'community', children: [{ id: 'community-home' }, { id: 'community-chat-history', primary: false, sectionId: 'community-chat' }] },
    { id: 'economy', children: [{ id: 'economy-home' }] },
    { id: 'system', children: [{ id: 'system-home' }] },
  ],
  routeParents: [],
}
'@ -Encoding UTF8 }
        else { Set-Content -LiteralPath $full -Value '<!-- CAPABILITY_MATURITY_START -->`n| ID | Owner | Contract | Anchor | Boundaries | Evidence | Maturity | Gate | Blockers/expiry |`n|---|---|---|---|---|---|---|---|---|`n<!-- CAPABILITY_MATURITY_END -->' -Encoding UTF8 }
    }
    New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'backend/src/Runtime/LSTY.SevenDPanel.Hosting') -Force | Out-Null
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Core/Large500.cs' -Content (New-ExactLineFixture 501 'Large500')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Core/Large800.cs' -Content (New-ExactLineFixture 801 'Large800')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Core/Large1000.cs' -Content (New-ExactLineFixture 1001 'Large1000')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Core/Equal500.cs' -Content (New-ExactLineFixture 500 'Equal500')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Core/Equal800.cs' -Content (New-ExactLineFixture 800 'Equal800')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Core/Equal1000.cs' -Content (New-ExactLineFixture 1000 'Equal1000')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Core/Contracts.cs' -Content @(
        'public interface IFixtureContract { }'
        'public interface ISecondFixtureContract { }'
    )
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlatformServiceRegistration.cs' -Content @(
        'public static class PlatformServiceRegistration { }'
    )
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/generated/GeneratedLarge.cs' -Content (New-LongFixture 1201 'public sealed class GeneratedLarge')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Adapters/Migrations/MigrationLarge.cs' -Content (New-LongFixture 1201 'public sealed class MigrationLarge')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/Adapters/Snapshots/SnapshotLarge.cs' -Content (New-LongFixture 1201 'public sealed class SnapshotLarge')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'backend/src/bin/ArtifactLarge.cs' -Content (New-LongFixture 1201 'public sealed class ArtifactLarge')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'frontend/apps/admin/src/features/overview/large400.ts' -Content (New-LongFixture 401 'const large400 =')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'frontend/apps/admin/src/features/overview/large600.ts' -Content (New-LongFixture 601 'const large600 =')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'frontend/apps/admin/src/features/overview/model/cross.ts' -Content @(
        "import { useAuthStore } from '../../auth/model/authStore'"
    )
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'frontend/apps/admin/src/features/overview/model/sideEffect.ts' -Content @(
        "import '../../auth/model/authStore'"
    )
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'frontend/apps/admin/src/features/overview/model/local.ts' -Content @(
        "import { overview } from '../index'"
    )
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'frontend/apps/admin/src/shared/api/generated/GeneratedLarge.ts' -Content (New-LongFixture 1201 'const generatedLarge =')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'frontend/apps/admin/src/route-map.d.ts' -Content (New-LongFixture 1201 'declare const generatedRoutes')
    Write-FixtureFile -Root $temporaryRoot -RelativePath 'docs/superpowers/README.md' -Content @(
        '# Change Records'
        '- Active: fixture activity record'
    )
    $metrics = Invoke-Json $measurePath $temporaryRoot
    Assert-True ($metrics.schemaVersion -eq 1) 'measure schema version missing.'
    Assert-True ($metrics.productProjectCount -eq 2) 'product project count fixture mismatch.'
    Assert-True ($metrics.firstLevelNavigationTaskCount -eq 6) 'navigation group count fixture mismatch.'
    Assert-True ($metrics.fixedNavigationEntryCount -eq 6) 'fixed navigation entry count fixture mismatch.'
    Assert-True ($metrics.backendTestProjectCount -eq 1) 'test project count fixture mismatch.'
    Assert-True ($metrics.productionCsFileCount -eq 9) 'production source count or exclusion fixture mismatch.'
    Assert-True ($metrics.productionHandwrittenFilesOver500Count -eq 5) 'production >500 fixture mismatch.'
    Assert-True ($metrics.productionHandwrittenFilesOver800Count -eq 3) 'production >800 fixture mismatch.'
    Assert-True ($metrics.productionHandwrittenFilesOver1000Count -eq 1) 'production >1000 fixture mismatch.'
    Assert-True ($metrics.adminHandwrittenFilesOver400Count -eq 2) 'Admin >400 fixture mismatch.'
    Assert-True ($metrics.adminHandwrittenFilesOver600Count -eq 1) 'Admin >600 fixture mismatch.'
    Assert-True ($metrics.registrationFileCount -eq 1) 'registration file count fixture mismatch.'
    Assert-True ($metrics.publicInterfaceTotalCount -eq 2) 'public interface count fixture mismatch.'
    Assert-True ($metrics.featureInternalCrossDomainImportCount -eq 2) 'cross-domain import count fixture mismatch.'
    Assert-True ($metrics.documentActivityRecordCount -eq 1) 'document activity count fixture mismatch.'
    Assert-True ($metrics.panelServiceProviderFactoryLineCount -eq $metrics.bootstrapRegistrationLineCount) 'factory line compatibility field mismatch.'
    Assert-True ($metrics.panelServiceProviderFactoryLineCount -gt 0) 'factory line count missing.'

    $metricsPath = Join-Path $temporaryRoot 'metrics.json'
    $metrics | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $metricsPath -Encoding UTF8

    Write-Host 'Complexity script self-tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
