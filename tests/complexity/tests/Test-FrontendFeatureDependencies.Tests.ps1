[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$auditScript = Join-Path $PSScriptRoot '..\Test-FrontendFeatureDependencies.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('seven-dpanel-frontend-dependencies-' + [guid]::NewGuid().ToString('N'))
$failures = [System.Collections.Generic.List[string]]::new()
$checkerMissing = -not (Test-Path -LiteralPath $auditScript -PathType Leaf)

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

function New-BaselineFixture {
    param([string] $Root)

    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/alpha/index.ts' -Content 'export const alpha = true'
    Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/beta/index.ts' -Content 'export const beta = true'
    Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/alpha/model/alpha.ts' -Content 'export const alphaModel = true'
    Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/beta/model/beta.ts' -Content 'export const betaModel = true'
    Write-FixtureFile -Root $Root -RelativePath 'tests/complexity/complexity-budget.json' -Content @'
{
  "featureDependencyAllowlist": []
}
'@
    Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/app/navigation/navigationCatalog.ts' -Content @'
export const navigationCatalog = {
  groups: [
    {
      id: 'alpha',
      children: [{ id: 'alpha-home' }],
    },
    {
      id: 'beta',
      children: [{ id: 'beta-home' }],
    },
  ],
  routeParents: [],
}
'@
}

function Invoke-Audit {
    param([string] $Root)

    if (-not (Test-Path -LiteralPath $auditScript -PathType Leaf)) {
        return [pscustomobject]@{
            ExitCode = 1
            Output = 'Test-FrontendFeatureDependencies.ps1 is missing.'
        }
    }

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $auditScript -RepositoryRoot $Root 2>&1)
        return [pscustomobject]@{
            ExitCode = [int] $LASTEXITCODE
            Output = ($output | Out-String -Width 4096)
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
        [scriptblock] $Mutate
    )

    $root = Join-Path $fixtureRoot $Name
    New-BaselineFixture -Root $root
    & $Mutate $root
    $result = Invoke-Audit -Root $root
    $actualExitCode = $result.ExitCode
    Write-Host ("{0}: actual={1}, expected={2}" -f $Name, $actualExitCode, $ExpectedExitCode)
    if ($actualExitCode -ne $ExpectedExitCode) {
        $failures.Add("$Name returned exit code $actualExitCode; expected $ExpectedExitCode. Output: $($result.Output.Trim())")
    }
    elseif ($ExpectedExitCode -ne 0 -and $result.Output -notmatch [regex]::Escape("[$ExpectedCategory]")) {
        $failures.Add("$Name returned the expected exit code but did not report [$ExpectedCategory]. Output: $($result.Output.Trim())")
    }
}

try {
    if ($checkerMissing) {
        Write-Host 'RED: Test-FrontendFeatureDependencies.ps1 is missing; valid baseline and violation scenarios are not claimed as verified.'
    }
    else {
        New-BaselineFixture -Root (Join-Path $fixtureRoot 'valid')
        $validResult = Invoke-Audit -Root (Join-Path $fixtureRoot 'valid')
        $validExitCode = $validResult.ExitCode
        Write-Host ("valid-baseline: actual={0}, expected=0" -f $validExitCode)
        if ($validExitCode -ne 0) {
            $failures.Add("valid baseline returned exit code $validExitCode; expected 0. Output: $($validResult.Output.Trim())")
        }

        Invoke-Scenario -Name 'allowlisted-cross-feature-import' -ExpectedExitCode 0 -ExpectedCategory '' -Mutate {
            param([string] $Root)
            Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/alpha/model/useAlpha.ts' -Content "import { betaModel } from '../../beta/model/beta'`nexport { betaModel }"
            Write-FixtureFile -Root $Root -RelativePath 'tests/complexity/complexity-budget.json' -Content @'
{
  "featureDependencyAllowlist": [
    {
      "importerPattern": "frontend/apps/admin/src/features/alpha/model/useAlpha.ts",
      "targetFeature": "beta",
      "specifierPattern": "../../beta/model/beta",
      "reason": "Fixture verifies an explicit stable read contract.",
      "owner": "fixture",
      "reviewAfter": "fixture review",
      "reviewCondition": "Remove when the contract is no longer needed."
    }
  ]
}
'@
        }

        Invoke-Scenario -Name 'unused-allowlist-rule' -ExpectedExitCode 1 -ExpectedCategory 'FeatureDependencyAllowlist' -Mutate {
            param([string] $Root)
            Write-FixtureFile -Root $Root -RelativePath 'tests/complexity/complexity-budget.json' -Content @'
{
  "featureDependencyAllowlist": [
    {
      "importerPattern": "frontend/apps/admin/src/features/alpha/model/useAlpha.ts",
      "targetFeature": "beta",
      "specifierPattern": "../../beta/model/missing",
      "reason": "Fixture verifies unused rules fail closed.",
      "owner": "fixture",
      "reviewAfter": "fixture review",
      "reviewCondition": "Remove stale rules."
    }
  ]
}
'@
        }

        Invoke-Scenario -Name 'cross-feature-import-from' -ExpectedExitCode 1 -ExpectedCategory 'FeatureInternalImport' -Mutate {
            param([string] $Root)
            Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/alpha/model/useAlpha.ts' -Content "import { betaModel } from '../../beta/model/beta'`nexport { betaModel }"
        }

        Invoke-Scenario -Name 'cross-feature-import-dynamic' -ExpectedExitCode 1 -ExpectedCategory 'FeatureInternalImport' -Mutate {
            param([string] $Root)
            Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/alpha/model/useAlpha.ts' -Content "export const loadBeta = () => import('../../beta/model/beta')"
        }

        Invoke-Scenario -Name 'cross-feature-import-side-effect' -ExpectedExitCode 1 -ExpectedCategory 'FeatureInternalImport' -Mutate {
            param([string] $Root)
            Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/features/alpha/model/useAlpha.ts' -Content "import '../../beta/model/beta'"
        }

        Invoke-Scenario -Name 'more-than-seven-fixed-entries' -ExpectedExitCode 1 -ExpectedCategory 'FixedNavigationEntries' -Mutate {
            param([string] $Root)
            Write-FixtureFile -Root $Root -RelativePath 'frontend/apps/admin/src/app/navigation/navigationCatalog.ts' -Content @'
export const navigationCatalog = {
  groups: [
    {
      id: 'alpha',
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
  ],
  routeParents: [],
}
'@
        }
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

if ($checkerMissing) {
    exit 1
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw 'Frontend dependency fail-closed fixture assertions failed.'
}

Write-Host 'Frontend dependency fail-closed fixtures passed.'
