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
$simplificationRoot = Join-Path $root 'docs\simplification'
$requiredFiles = @('README.md', 'inventory.md', 'roadmap.md')
$validCandidateStatuses = @(
    (-join ([char[]](0x5F85, 0x8C03, 0x67E5))),
    (-join ([char[]](0x8C03, 0x67E5, 0x4E2D))),
    (-join ([char[]](0x5DF2, 0x786E, 0x8BA4))),
    (-join ([char[]](0x5B9E, 0x65BD, 0x4E2D))),
    (-join ([char[]](0x5DF2, 0x5B8C, 0x6210))),
    (-join ([char[]](0x4FDD, 0x7559))),
    (-join ([char[]](0x6682, 0x7F13))),
    (-join ([char[]](0x64A4, 0x9500))),
    'pending', 'investigating', 'confirmed', 'in-progress', 'completed', 'retain', 'deferred', 'revoked'
)
$validPhaseStatuses = @(
    (-join ([char[]](0x672A, 0x5F00, 0x59CB))),
    (-join ([char[]](0x8FDB, 0x884C, 0x4E2D))),
    (-join ([char[]](0x5DF2, 0x5B8C, 0x6210))),
    'not-started', 'in-progress', 'completed'
)

function Fail([string] $Message) {
    throw "Simplification documentation check failed: $Message"
}

if (-not (Test-Path -LiteralPath $simplificationRoot -PathType Container)) {
    Fail "missing docs/simplification directory: $simplificationRoot"
}

foreach ($file in $requiredFiles) {
    $path = Join-Path $simplificationRoot $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Fail "missing required file: $path"
    }
}

function Test-LocalLink([string] $DocumentPath, [string] $Target) {
    $value = $Target.Trim()
    if ([string]::IsNullOrWhiteSpace($value) -or $value.StartsWith('#')) {
        return
    }
    if ($value -match '^(?i)(https?|mailto):') {
        return
    }
    $value = $value.Split('#')[0].Split('?')[0]
    if ([string]::IsNullOrWhiteSpace($value)) {
        return
    }
    if ([System.IO.Path]::IsPathRooted($value) -or $value -match '^[A-Za-z]:') {
        Fail "$DocumentPath contains an absolute link: $Target"
    }
    $resolved = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $DocumentPath) $value))
    if (-not (Test-Path -LiteralPath $resolved)) {
        Fail "$DocumentPath contains a broken local link: $Target"
    }
}

foreach ($document in @(Get-ChildItem -LiteralPath $simplificationRoot -Filter '*.md' -File)) {
    $content = Get-Content -LiteralPath $document.FullName -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($content, '\[[^\]]+\]\((?<target>[^)]+)\)')) {
        Test-LocalLink $document.FullName $match.Groups['target'].Value
    }
}

$inventoryPath = Join-Path $simplificationRoot 'inventory.md'
$inventory = Get-Content -LiteralPath $inventoryPath -Raw -Encoding UTF8
$detailedIds = @([regex]::Matches($inventory, '(?m)^#{3,6}\s+(?<id>SIM-\d+)') | ForEach-Object { $_.Groups['id'].Value })
if ($detailedIds.Count -eq 0) {
    Fail 'inventory has no detailed SIM candidate headings.'
}
if (@($detailedIds | Sort-Object -Unique).Count -ne $detailedIds.Count) {
    Fail 'detailed SIM candidate IDs must be unique.'
}
$statusLabelPattern = (-join ([char[]](0x72B6, 0x6001))) + ':'
$statusMatches = [regex]::Matches($inventory, "(?m)^-\s*$statusLabelPattern\s*`?(?<status>[^`\r\n]+)`?\s*$")
foreach ($statusMatch in $statusMatches) {
    $status = $statusMatch.Groups['status'].Value.Trim()
    if ($validCandidateStatuses -notcontains $status) {
        Fail "inventory contains unknown candidate/sample status: $status"
    }
}

$roadmapPath = Join-Path $simplificationRoot 'roadmap.md'
$roadmap = Get-Content -LiteralPath $roadmapPath -Raw -Encoding UTF8
$phaseRows = @([regex]::Matches($roadmap, '(?m)^\|\s*(?<id>[1-6])\s*\|\s*[^|]+\s*\|\s*(?<status>[^|]+)\s*\|') | ForEach-Object {
    [pscustomobject]@{
        Id = [int]$_.Groups['id'].Value
        Status = $_.Groups['status'].Value.Trim()
    }
})
if ($phaseRows.Count -ne 6 -or @($phaseRows.Id | Sort-Object -Unique).Count -ne 6) {
    Fail "roadmap must contain exactly one table row for each phase 1-6; found $($phaseRows.Count) rows."
}
foreach ($row in $phaseRows) {
    if ($validPhaseStatuses -notcontains $row.Status) {
        Fail "roadmap phase $($row.Id) has unknown status: $($row.Status)"
    }
}

$phaseLabel = -join ([char[]](0x9636, 0x6BB5))
$phaseNumerals = @(
    [char]0x4E00,
    [char]0x4E8C,
    [char]0x4E09,
    [char]0x56DB,
    [char]0x4E94,
    [char]0x516D
)
$roadmapLines = @($roadmap -split "`r?`n")
$phaseHeadingIndexes = @{}
for ($phase = 1; $phase -le 6; $phase++) {
    $chinesePrefix = '# ' + $phaseLabel + $phaseNumerals[$phase - 1]
    $englishHeading = "# Phase $phase"
    $matches = @()
    for ($lineIndex = 0; $lineIndex -lt $roadmapLines.Count; $lineIndex++) {
        $line = $roadmapLines[$lineIndex]
        if ($line.StartsWith($chinesePrefix, [System.StringComparison]::Ordinal) -or
            $line.Equals($englishHeading, [System.StringComparison]::Ordinal)) {
            $matches += $lineIndex
        }
    }
    if ($matches.Count -ne 1) {
        Fail "roadmap must contain exactly one heading for phase $phase; found $($matches.Count)."
    }
    $phaseHeadingIndexes[$phase] = $matches[0]
}

$completedStatuses = @(
    (-join ([char[]](0x5DF2, 0x5B8C, 0x6210))),
    'completed'
)
for ($phase = 1; $phase -le 6; $phase++) {
    $start = [int]$phaseHeadingIndexes[$phase]
    $end = if ($phase -lt 6) { [int]$phaseHeadingIndexes[$phase + 1] } else { $roadmapLines.Count }
    $block = ($roadmapLines[$start..($end - 1)] -join "`n")
    $row = @($phaseRows | Where-Object Id -eq $phase)[0]
    if ($completedStatuses -contains $row.Status -and $block -match '(?m)^-\s+\[\s\]') {
        Fail "roadmap phase $phase is completed but still contains unchecked work items."
    }
}

$readme = Get-Content -LiteralPath (Join-Path $simplificationRoot 'README.md') -Raw -Encoding UTF8
$goldenPathHeadingCount = [regex]::Matches($readme, '(?m)^###\s+').Count
if ($goldenPathHeadingCount -lt 3) {
    Fail "README must contain at least three level-three Golden Path headings; found $goldenPathHeadingCount."
}
foreach ($anchor in @('CAP-01', 'CAP-07', 'CAP-03')) {
    if ($readme.IndexOf($anchor, [System.StringComparison]::Ordinal) -lt 0) {
        Fail "README is missing production Golden Path anchor: $anchor"
    }
}

Write-Host "Simplification documentation check passed: files=$($requiredFiles.Count), candidates=$($detailedIds.Count), phases=6."
