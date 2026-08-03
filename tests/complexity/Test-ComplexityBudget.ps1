[CmdletBinding()]
param([string] $RepositoryRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) { $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition }
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = Join-Path $scriptDirectory '..\..' }
$measurePath = Join-Path $scriptDirectory 'Measure-Complexity.ps1'
$metrics = (& $measurePath -RepositoryRoot $RepositoryRoot | ConvertFrom-Json)

$baseline = [ordered]@{
    productProjectCount = 8
    backendTestProjectCount = 1
    compositionRootCount = 1
    firstLevelNavigationTaskCount = 6
    hostingApplicationProjectReferences = 1
    unknownCapabilityCount = 0
    newPublicInterfaceCount = 0
}
$violations = @()
foreach ($name in @('productProjectCount','backendTestProjectCount','compositionRootCount','firstLevelNavigationTaskCount','hostingApplicationProjectReferences','unknownCapabilityCount','newPublicInterfaceCount')) {
    $actual = [int] $metrics.$name
    $limit = [int] $baseline[$name]
    if ($actual -gt $limit) { $violations += "$name=$actual exceeds baseline $limit" }
}
if ($metrics.productionCsFileCount -lt 1 -or $metrics.backendTestFileCount -lt 1 -or $metrics.adminFeatureCount -lt 1) {
    $violations += 'required source/test/feature counts are missing'
}
if ($metrics.bootstrapRegistrationLineCount -lt 1) { $violations += 'bootstrap registration file was not measured' }

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Complexity budget passed: projects=$($metrics.productProjectCount), tests=$($metrics.backendTestProjectCount), compositionRoots=$($metrics.compositionRootCount), navigationTasks=$($metrics.firstLevelNavigationTaskCount)."
