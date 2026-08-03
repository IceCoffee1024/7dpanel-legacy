$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$complexityRoot = Split-Path $PSScriptRoot -Parent
$measurePath = Join-Path $complexityRoot 'Measure-Complexity.ps1'
$budgetPath = Join-Path $complexityRoot 'Test-ComplexityBudget.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-complexity-' + [Guid]::NewGuid().ToString('N'))

function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Invoke-Json([string] $Path, [string] $Root) { return (& $Path -RepositoryRoot $Root | ConvertFrom-Json) }

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
        elseif ($path -like '*.ts') { Set-Content -LiteralPath $full -Value "      id: 'overview',`n      id: 'operations',`n      id: 'players',`n      id: 'community',`n      id: 'economy',`n      id: 'system',`n  routeParents: [" -Encoding UTF8 }
        else { Set-Content -LiteralPath $full -Value '<!-- CAPABILITY_MATURITY_START -->`n| ID | Owner | Contract | Anchor | Boundaries | Evidence | Maturity | Gate | Blockers/expiry |`n|---|---|---|---|---|---|---|---|---|`n<!-- CAPABILITY_MATURITY_END -->' -Encoding UTF8 }
    }
    New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'backend/src/Runtime/LSTY.SevenDPanel.Hosting') -Force | Out-Null
    $metrics = Invoke-Json $measurePath $temporaryRoot
    Assert-True ($metrics.schemaVersion -eq 1) 'measure schema version missing.'
    Assert-True ($metrics.productProjectCount -eq 2) 'product project count fixture mismatch.'
    Assert-True ($metrics.firstLevelNavigationTaskCount -eq 6) 'navigation group count fixture mismatch.'
    Assert-True ($metrics.backendTestProjectCount -eq 1) 'test project count fixture mismatch.'
    Assert-True ($metrics.productionCsFileCount -ge 1) 'production source count missing.'

    $metricsPath = Join-Path $temporaryRoot 'metrics.json'
    $metrics | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $metricsPath -Encoding UTF8

    Write-Host 'Complexity script self-tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
