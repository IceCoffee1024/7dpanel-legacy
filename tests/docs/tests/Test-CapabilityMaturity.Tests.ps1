$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Test-CapabilityMaturity.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-capability-maturity-' + [Guid]::NewGuid().ToString('N'))
$docsRoot = Join-Path $temporaryRoot 'docs'
$testDocument = Join-Path $docsRoot 'test.md'
$designDocument = Join-Path $docsRoot 'design.md'
$anchorDocument = Join-Path $docsRoot 'anchor.md'

function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Write-Fixture([string] $Table) {
    New-Item -ItemType Directory -Path $docsRoot -Force | Out-Null
    Set-Content -LiteralPath $designDocument -Value '# Design' -Encoding UTF8
    Set-Content -LiteralPath $anchorDocument -Value '# Anchor' -Encoding UTF8
    Set-Content -LiteralPath $testDocument -Value @("# Test", '<!-- CAPABILITY_MATURITY_START -->', $Table, '<!-- CAPABILITY_MATURITY_END -->') -Encoding UTF8
}
function Invoke-Check([bool] $ExpectSuccess) {
    $success = $true
    try { & $scriptPath -RepositoryRoot $temporaryRoot | Out-Null } catch { $success = $false }
    Assert-True ($success -eq $ExpectSuccess) "Expected maturity checker success=$ExpectSuccess, got $success."
}
function New-ValidRows {
    $owners = @('Operations','Players','Operations','Community','Administration','Operations','Community','Players','Economy','Community','Administration','Operations','Operations','Players','Operations')
    $ids = @('CAP-01','CAP-02','CAP-03','CAP-04','CAP-05','CAP-06','CAP-07','CAP-08','CAP-09','CAP-10','CAP-11','CAP-12','J1','J2','J3')
    $rows = @('| ID | Owner | Contract | Current implementation anchor | Required boundaries | Evidence | Maturity | Gate | Blockers/expiry |','|---|---|---|---|---|---|---|---|---|')
    for ($i = 0; $i -lt $ids.Count; $i++) {
        $rows += "| $($ids[$i]) | $($owners[$i]) | contract | docs/anchor.md | Application | docs/design.md | Implemented | Open | pending evidence |"
    }
    return ($rows -join "`n")
}

try {
    Write-Fixture (New-ValidRows)
    Invoke-Check $true

    $table = (New-ValidRows).Replace('| J3 |', '| CAP-01 |')
    Write-Fixture $table
    Invoke-Check $false

    $table = (New-ValidRows).Replace('| CAP-01 | Operations |', '| CAP-01 | Unknown |')
    Write-Fixture $table
    Invoke-Check $false

    $table = (New-ValidRows).Replace('| CAP-01 | Operations | contract | docs/anchor.md | Application | docs/design.md | Implemented | Open | pending evidence |', '| CAP-01 | Operations | contract | docs/anchor.md | Application | docs/design.md | Verified | Passed | artifact missing |')
    Write-Fixture $table
    Invoke-Check $false

    $table = (New-ValidRows).Replace('| CAP-01 | Operations | contract | docs/anchor.md | Application | docs/design.md | Implemented | Open | pending evidence |', '| CAP-01 | Operations | contract | docs/missing.md | Application | docs/design.md | Implemented | Open | pending evidence |')
    Write-Fixture $table
    Invoke-Check $false

    Write-Host 'Capability maturity checker self-tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
