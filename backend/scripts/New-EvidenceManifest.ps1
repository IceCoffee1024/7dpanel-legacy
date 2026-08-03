[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string] $EvidenceKind,
    [string] $GitCommit,
    [switch] $GitDirty,
    [object] $ArtifactIdentity,
    [string] $ProductVersion,
    [string] $GameVersion,
    [string] $OperatingSystem,
    [string] $BrowserVersion,
    [string] $EnvironmentId,
    [string] $ExecutionScope,
    [ValidateSet('Running', 'Passed', 'Failed', 'Skipped')]
    [string] $Status = 'Running',
    [string[]] $SubEvidencePaths = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Get-Sha256Hex {
    param([byte[]] $Bytes)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { return (($algorithm.ComputeHash($Bytes) | ForEach-Object { $_.ToString('X2') }) -join '') }
    finally { $algorithm.Dispose() }
}

function Assert-EvidenceRelativePath {
    param([string] $Value)
    $normalized = $Value.Replace('\', '/')
    $segments = @($normalized.Split('/') | Where-Object { $_.Length -gt 0 })
    if ([string]::IsNullOrWhiteSpace($Value) -or [System.IO.Path]::IsPathRooted($Value) -or $normalized.StartsWith('/') -or
        $normalized -match '^[A-Za-z]:' -or $normalized.Contains(':') -or $segments.Count -eq 0 -or
        @($segments | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0) {
        throw "Evidence sub-path is unsafe: $Value"
    }
    return $normalized
}

function Get-RepositoryGitValue {
    param([string[]] $Arguments)
    try {
        $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        $output = @(& git -C $repoRoot @Arguments 2>$null)
        if ($LASTEXITCODE -eq 0) { return ($output -join "`n").Trim() }
    }
    catch { }
    return $null
}

$supportedKinds = @('release-smoke', 'candidate-release', 'restore-drill', 'browser-journey', 'operations-lane')
if ($supportedKinds -notcontains $EvidenceKind) { throw "Unsupported evidence kind: $EvidenceKind" }

$inputDirectory = Get-Item -LiteralPath $EvidenceDirectory -Force -ErrorAction Stop
if ($inputDirectory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Evidence directory must not be a reparse point: $EvidenceDirectory" }
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedDirectory.ProviderPath -PathType Container)) { throw "Evidence directory is not a directory: $EvidenceDirectory" }
$evidenceRoot = [System.IO.Path]::GetFullPath($resolvedDirectory.ProviderPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$volumeRoot = [System.IO.Path]::GetPathRoot($evidenceRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if ($evidenceRoot -eq $volumeRoot) { throw "Evidence directory must not be a filesystem root: $EvidenceDirectory" }
if ((Get-Item -LiteralPath $evidenceRoot -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Evidence directory must not be a reparse point: $EvidenceDirectory" }

$commit = if ($PSBoundParameters.ContainsKey('GitCommit')) { $GitCommit } else { Get-RepositoryGitValue @('rev-parse', 'HEAD') }
$dirty = if ($PSBoundParameters.ContainsKey('GitDirty')) { $GitDirty.IsPresent } else { [bool] (Get-RepositoryGitValue @('status', '--porcelain')) }
$candidate = $EvidenceKind -eq 'candidate-release'
if ($candidate -and ([string]::IsNullOrWhiteSpace($commit) -or $commit -notmatch '^[0-9A-Fa-f]{40}$')) { throw 'Candidate evidence requires a Git commit.' }
if ($candidate -and $dirty) { throw 'Candidate evidence requires a clean Git working tree.' }

$artifactSha256 = $null
if ($null -ne $ArtifactIdentity) {
    $property = $ArtifactIdentity.PSObject.Properties['artifactSha256']
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string] $property.Value) -or [string] $property.Value -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Artifact identity must contain a valid SHA-256.'
    }
    $artifactSha256 = ([string] $property.Value).ToUpperInvariant()
}
if ($candidate -and $null -eq $artifactSha256) { throw 'Candidate evidence requires an artifact identity.' }
foreach ($field in @('ProductVersion', 'GameVersion', 'OperatingSystem', 'BrowserVersion', 'EnvironmentId', 'ExecutionScope')) {
    if ($candidate -and [string]::IsNullOrWhiteSpace((Get-Variable -Name $field -ValueOnly))) { throw "Candidate evidence requires $field." }
}
if ($candidate -and $Status -eq 'Skipped') { throw 'Candidate evidence cannot be skipped.' }
if ($candidate -and $Status -ne 'Passed') { throw 'Candidate evidence must be Passed.' }

$subEvidence = @()
$seenEvidence = @{}
foreach ($path in $SubEvidencePaths) {
    $normalized = Assert-EvidenceRelativePath $path
    $key = $normalized.ToUpperInvariant()
    if ($seenEvidence.ContainsKey($key)) { throw "Evidence sub-path is duplicated: $normalized" }
    $seenEvidence[$key] = $true
    $subEvidence += $normalized
}

$environmentDigestSource = if ([string]::IsNullOrWhiteSpace($EnvironmentId)) { 'unspecified' } else { $EnvironmentId }
$manifest = [ordered]@{
    schemaVersion = 1
    evidenceKind = $EvidenceKind
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    gitCommit = $commit
    gitDirty = $dirty
    artifactSha256 = $artifactSha256
    productVersion = $ProductVersion
    gameVersion = $GameVersion
    operatingSystem = $OperatingSystem
    browserVersion = $BrowserVersion
    environmentId = Get-Sha256Hex ([System.Text.Encoding]::UTF8.GetBytes($environmentDigestSource))
    executionScope = $ExecutionScope
    status = $Status
    maturity = if ($candidate -and $Status -eq 'Passed') { 'Candidate' } else { 'Development' }
    subEvidence = $subEvidence
}

$outputPath = Join-Path $evidenceRoot 'manifest.json'
if ((Test-Path -LiteralPath $outputPath) -and ((Get-Item -LiteralPath $outputPath -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
    throw "Evidence manifest path must not be a reparse point: $outputPath"
}
$json = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($outputPath, $json, (New-Object System.Text.UTF8Encoding($false)))
[pscustomobject] $manifest
