[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ServerRoot,
    [Parameter(Mandatory = $true)] [string] $ExpectedWorldName,
    [Parameter(Mandatory = $true)] [string] $EnvironmentId,
    [Parameter(Mandatory = $true)] [string] $EvidenceDirectory,
    [string] $IsolationRoot,
    [string] $BackupId,
    [switch] $CreateBackup,
    [switch] $ConfirmDestructiveRestoreDrill
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Fail([string] $Message) { throw "Restore drill preflight failed: $Message" }
function Resolve-SafeDirectory([string] $Path, [string] $Name) {
    if ([string]::IsNullOrWhiteSpace($Path)) { Fail "$Name is required." }
    $full = [System.IO.Path]::GetFullPath($Path)
    $trimmed = $full.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $root = [System.IO.Path]::GetPathRoot($full).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($trimmed -eq $root) { Fail "$Name must not be a filesystem root." }
    $userProfile = if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) { $null } else { [System.IO.Path]::GetFullPath($env:USERPROFILE).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) }
    if (($null -ne $userProfile -and $trimmed -eq $userProfile) -or $trimmed -match '(?i)\\(Windows|Program Files)(\\|$)') { Fail "$Name is a protected system/user path." }
    if (Test-Path -LiteralPath $full -PathType Leaf) { Fail "$Name must be a directory." }
    if ((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) { Fail "$Name must not be a reparse point." }
    return $full
}
function Is-Under([string] $Child, [string] $Parent) {
    $childFull = [System.IO.Path]::GetFullPath($Child).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    return $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)
}
function Write-Json([string] $Path, [object] $Value) { [System.IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false))) }

if ($ExpectedWorldName -notmatch '^[^\\/:*?"<>|]{1,128}$') { Fail 'ExpectedWorldName contains unsafe path characters.' }
if ($EnvironmentId -notmatch '^[^\s]{1,200}$') { Fail 'EnvironmentId must be a non-empty environment identifier.' }
if ([string]::IsNullOrWhiteSpace($BackupId) -and -not $CreateBackup) { Fail 'Specify BackupId or CreateBackup.' }
if (-not $ConfirmDestructiveRestoreDrill) { Fail 'ConfirmDestructiveRestoreDrill is required; no files were touched.' }
$server = Resolve-SafeDirectory $ServerRoot 'ServerRoot'
$evidence = Resolve-SafeDirectory $EvidenceDirectory 'EvidenceDirectory'
if ([string]::IsNullOrWhiteSpace($IsolationRoot)) { Fail 'IsolationRoot is required for a destructive drill.' }
$isolation = Resolve-SafeDirectory $IsolationRoot 'IsolationRoot'
if (-not (Is-Under $server $isolation)) { Fail 'ServerRoot must be inside IsolationRoot.' }
if (-not (Is-Under $evidence $isolation)) { Fail 'EvidenceDirectory must be inside IsolationRoot.' }
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (Is-Under $server $repositoryRoot -or Is-Under $evidence $repositoryRoot) { Fail 'Destructive drill paths must not be inside the repository.' }

New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$run = Join-Path $evidence ('restore-drill-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $run | Out-Null
$preflight = [ordered]@{
    schemaVersion = 1; journey = 'J3'; status = 'Skipped'; environmentId = $EnvironmentId
    expectedWorldName = $ExpectedWorldName; serverRoot = $server; isolationRoot = $isolation
    evidenceDirectory = $run; backupId = $BackupId; createBackup = $CreateBackup.IsPresent
    destructiveConfirmation = $ConfirmDestructiveRestoreDrill.IsPresent
    reason = 'Preflight passed. Destructive execution is reserved for the frozen candidate lane.'
}
$preflightPath = Join-Path $run 'preflight.json'
Write-Json $preflightPath $preflight
$manifestScript = Join-Path $PSScriptRoot '..\..\backend\scripts\New-EvidenceManifest.ps1'
if (Test-Path -LiteralPath $manifestScript -PathType Leaf) {
    & $manifestScript -EvidenceDirectory $run -EvidenceKind 'restore-drill' -EnvironmentId $EnvironmentId -ExecutionScope 'restore-drill-preflight' -Status 'Skipped' -SubEvidencePaths @('preflight.json') | Out-Null
}
Write-Output (Get-Content -LiteralPath $preflightPath -Raw)
exit 2
