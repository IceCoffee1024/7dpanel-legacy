[CmdletBinding()]
param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
}
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $scriptDirectory '..\..'
}
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$documentPath = Join-Path $root 'docs\test.md'
$expectedIds = @('CAP-01','CAP-02','CAP-03','CAP-04','CAP-05','CAP-06','CAP-07','CAP-08','CAP-09','CAP-10','CAP-11','CAP-12','J1','J2','J3')
$validOwners = @('Platform','Operations','Players','Community','Economy','Automation','Administration')
$validMaturity = @('Planned','Implemented','Verified','Release-ready')
$validGates = @('Open','Blocked','Passed')

function Fail([string] $Message) { throw "Capability maturity check failed: $Message" }

if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) { Fail "missing docs/test.md: $documentPath" }
$content = Get-Content -LiteralPath $documentPath -Raw
$match = [regex]::Match($content, '(?s)<!--\s*CAPABILITY_MATURITY_START\s*-->(?<table>.*?)<!--\s*CAPABILITY_MATURITY_END\s*-->')
if (-not $match.Success) { Fail 'CAPABILITY_MATURITY_START/END markers are missing or out of order.' }

$lines = @($match.Groups['table'].Value -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($lines.Count -lt 3) { Fail 'maturity table is empty or missing its header/separator.' }
$header = @($lines[0].Trim('|').Split('|') | ForEach-Object { $_.Trim() })
$requiredHeader = @('ID','Owner','Contract','Current implementation anchor','Required boundaries','Evidence','Maturity','Gate','Blockers/expiry')
if (($header -join '|') -ne ($requiredHeader -join '|')) { Fail "maturity table header must be: $($requiredHeader -join ' | ')" }
if ($lines[1] -notmatch '^\|?\s*:?-{3,}') { Fail 'maturity table separator is missing.' }

$rows = @()
foreach ($line in @($lines | Select-Object -Skip 2)) {
    if ($line.Trim() -notmatch '^\|') { Fail "maturity row is not a pipe table row: $line" }
    $fields = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
    if ($fields.Count -ne $requiredHeader.Count) { Fail "maturity row has $($fields.Count) fields instead of $($requiredHeader.Count): $line" }
    $rows += [pscustomobject]([ordered]@{
        Id = $fields[0]; Owner = $fields[1]; Contract = $fields[2]
        Anchor = $fields[3]; Boundaries = $fields[4]; Evidence = $fields[5]
        Maturity = $fields[6]; Gate = $fields[7]; Blockers = $fields[8]
    })
}

if ($rows.Count -ne $expectedIds.Count) { Fail "expected $($expectedIds.Count) rows, found $($rows.Count)." }
if (@($rows.Id | Sort-Object -Unique).Count -ne $rows.Count) { Fail 'maturity IDs must be unique.' }
foreach ($id in $expectedIds) { if (@($rows | Where-Object Id -eq $id).Count -ne 1) { Fail "missing required maturity ID: $id" } }

function Test-RepoTarget([string] $Target) {
    $value = $Target.Trim().Trim('<','>')
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match '^(https?|mailto):') { return $true }
    $value = $value.Split('#')[0].Split('?')[0]
    if ([string]::IsNullOrWhiteSpace($value)) { return $true }
    if ([System.IO.Path]::IsPathRooted($value) -or $value -match '^[A-Za-z]:') { return $false }
    return Test-Path -LiteralPath (Join-Path $root $value)
}

foreach ($row in $rows) {
    if ($validOwners -notcontains $row.Owner) { Fail "$($row.Id) has unknown owner '$($row.Owner)'." }
    if ($validMaturity -notcontains $row.Maturity) { Fail "$($row.Id) has unknown maturity '$($row.Maturity)'." }
    if ($validGates -notcontains $row.Gate) { Fail "$($row.Id) has unknown gate '$($row.Gate)'." }
    if ([string]::IsNullOrWhiteSpace($row.Contract) -or [string]::IsNullOrWhiteSpace($row.Anchor) -or [string]::IsNullOrWhiteSpace($row.Boundaries)) { Fail "$($row.Id) has an empty required field." }
    if ($row.Anchor -match '(?i)(target-blueprint|superpowers[\\/]spec|superpowers[\\/]plan|implementation plan|design spec)') { Fail "$($row.Id) uses a Target/spec/plan as its implementation anchor." }
    $anchorTargets = @([regex]::Matches($row.Anchor, '`(?<target>[^`]+)`|\]\((?<target2>[^)]+)\)') | ForEach-Object {
        if ($_.Groups['target'].Success) { $_.Groups['target'].Value } else { $_.Groups['target2'].Value }
    })
    if ($anchorTargets.Count -eq 0) { $anchorTargets = @($row.Anchor.Trim()) }
    foreach ($target in $anchorTargets) {
        if (-not (Test-RepoTarget $target)) { Fail "$($row.Id) implementation anchor does not resolve: $target" }
    }
    if ([string]::IsNullOrWhiteSpace($row.Evidence)) { Fail "$($row.Id) has no evidence reference." }
    $markdownTargets = @([regex]::Matches($row.Evidence, '\]\((?<target>[^)]+)\)') | ForEach-Object { $_.Groups['target'].Value })
    foreach ($target in $markdownTargets) { if (-not (Test-RepoTarget $target)) { Fail "$($row.Id) evidence link does not resolve: $target" } }
    if ($row.Maturity -in @('Verified','Release-ready') -and $row.Evidence -notmatch '(?i)\b[A-F0-9]{64}\b|artifact(?:Sha256| hash)|manifest') { Fail "$($row.Id) $($row.Maturity) evidence must include an artifact SHA-256 or manifest reference." }
    if ($row.Maturity -eq 'Release-ready' -and ($row.Gate -ne 'Passed' -or $row.Blockers -notmatch '^(?:-|None|无)$')) { Fail "$($row.Id) Release-ready rows must have Passed gate and no blocker." }
    if ($row.Gate -eq 'Blocked' -and [string]::IsNullOrWhiteSpace($row.Blockers)) { Fail "$($row.Id) Blocked rows must declare a blocker or expiry." }
    if ($row.Gate -eq 'Passed' -and $row.Evidence -match '(?i)\bskipped\b|\bfailed\b') { Fail "$($row.Id) skipped/failed evidence cannot count as Passed." }
}

Write-Host "Capability maturity check passed for $($rows.Count) rows."
