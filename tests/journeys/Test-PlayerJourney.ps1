[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ExpectedCrossplatformId,
    [Parameter(Mandatory = $true)] [string] $EnvironmentId,
    [Parameter(Mandatory = $true)] [string] $EvidenceDirectory,
    [switch] $ConfirmKickTestPlayer,
    [string] $ApiBaseUrl = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Fail([string] $Message) { throw "Player journey preflight failed: $Message" }
function Assert-SafeDirectory([string] $Path, [string] $Name) {
    if ([string]::IsNullOrWhiteSpace($Path)) { Fail "$Name is required." }
    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($full).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($full.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -eq $root) { Fail "$Name must not be a filesystem root." }
    if (Test-Path -LiteralPath $full -PathType Leaf) { Fail "$Name must be a directory." }
    if ((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) { Fail "$Name must not be a reparse point." }
    return $full
}
function Get-Fingerprint([string] $Value) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha256.Dispose(); $bytes = $null }
}
function Write-Json([string] $Path, [object] $Value) {
    [System.IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
}
function Write-Evidence([string] $Status) {
    Write-Json $summaryPath $summary
    $manifestScript = Join-Path $PSScriptRoot '..\..\backend\scripts\New-EvidenceManifest.ps1'
    if (Test-Path -LiteralPath $manifestScript -PathType Leaf) {
        & $manifestScript -EvidenceDirectory $runDirectory -EvidenceKind 'browser-journey' -EnvironmentId $EnvironmentId -ExecutionScope 'player-journey' -Status $Status -SubEvidencePaths @('summary.json') | Out-Null
    }
}
function Stop-Skipped([string] $Reason) {
    $summary.status = 'Skipped'; $summary.reason = $Reason
    Write-Evidence 'Skipped'
    Write-Output (Get-Content -LiteralPath $summaryPath -Raw)
    exit 2
}
function Stop-Blocked([string] $Reason) {
    $summary.status = 'Blocked'; $summary.reason = $Reason
    Write-Evidence 'Skipped'
    Write-Output (Get-Content -LiteralPath $summaryPath -Raw)
    exit 2
}
function Invoke-PanelJson([string] $Method, [string] $Uri, [hashtable] $Headers, [object] $Body = $null) {
    $parameters = @{ Method = $Method; Uri = $Uri; Headers = $Headers; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ($Body | ConvertTo-Json -Depth 4 -Compress)
    }
    return Invoke-RestMethod @parameters
}
function Get-OnlineMatch([object[]] $Players) {
    return @($Players | Where-Object {
        $identity = $_.crossplatformIdentity
        $null -ne $identity -and $identity.combinedId -ceq $ExpectedCrossplatformId
    })
}

if ($ExpectedCrossplatformId -notmatch '^[^\s]{1,200}$') { Fail 'ExpectedCrossplatformId must be one non-empty stable identity.' }
if ($EnvironmentId -notmatch '^[^\s]{1,200}$') { Fail 'EnvironmentId must be a non-empty environment identifier.' }
if ($ApiBaseUrl -notmatch '^https?://[^\s/]+') { Fail 'ApiBaseUrl must be an absolute HTTP(S) URL.' }
$evidenceRoot = Assert-SafeDirectory $EvidenceDirectory 'EvidenceDirectory'
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
$runDirectory = Join-Path $evidenceRoot ('player-journey-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$summary = [ordered]@{
    schemaVersion = 1; journey = 'J2'; environmentId = $EnvironmentId
    expectedCrossplatformIdFingerprint = Get-Fingerprint $ExpectedCrossplatformId
    status = 'Running'; steps = @()
}
$summaryPath = Join-Path $runDirectory 'summary.json'

$token = $env:PANEL_ACCESS_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    Stop-Skipped 'PANEL_ACCESS_TOKEN is not set; no real player API call was attempted.'
}
if (-not $ConfirmKickTestPlayer) {
    Stop-Skipped 'ConfirmKickTestPlayer was not supplied; no player action was attempted.'
}

$headers = @{ Authorization = "Bearer $token" }
$onlineUri = ($ApiBaseUrl.TrimEnd('/') + '/api/v1/players/online')
try { $response = Invoke-PanelJson 'GET' $onlineUri $headers }
catch { Stop-Blocked 'Online player query failed; no player action was attempted.' }

$matches = Get-OnlineMatch @($response.players)
if ($matches.Count -eq 0) { Stop-Skipped 'Expected stable player identity is not connected.' }
if ($matches.Count -ne 1) { Stop-Blocked 'Stable player identity is ambiguous.' }
$player = $matches[0]
if ($null -eq $player.platformIdentity -or [string]::IsNullOrWhiteSpace($player.platformIdentity.combinedId) -or [string]::IsNullOrWhiteSpace($player.platformIdentity.platform)) {
    Stop-Blocked 'Matched player does not have a valid platform identity.'
}
if ($player.entityId -isnot [int] -or $player.entityId -lt 0) { Stop-Blocked 'Matched player does not have a valid entity ID.' }
$fixedTarget = [ordered]@{
    entityId = [int] $player.entityId
    platformIdentity = [ordered]@{ combinedId = [string] $player.platformIdentity.combinedId; platform = [string] $player.platformIdentity.platform }
}
$summary.steps += [ordered]@{ name = 'online-players'; status = 'Passed'; observedAtUtc = $player.observedAtUtc }

$kickUri = $ApiBaseUrl.TrimEnd('/') + '/api/v1/players/' + $fixedTarget.entityId + '/kick'
try {
    $kick = Invoke-PanelJson 'POST' $kickUri $headers ([ordered]@{
        expectedPlatformIdentity = $fixedTarget.platformIdentity
        reason = 'Controlled J2 verification'
        confirmed = $true
    })
}
catch { Stop-Blocked 'Kick request failed; inspect the server-side audit using the controlled environment.' }
if ($kick.status -cne 'succeeded' -or [string]::IsNullOrWhiteSpace($kick.operationId) -or $kick.target.entityId -ne $fixedTarget.entityId -or $kick.target.platformIdentity.combinedId -cne $fixedTarget.platformIdentity.combinedId -or $kick.target.platformIdentity.platform -cne $fixedTarget.platformIdentity.platform) {
    Stop-Blocked 'Kick response did not confirm the fixed target and terminal result.'
}
$summary.steps += [ordered]@{ name = 'kick'; status = 'Passed'; operationId = $kick.operationId; terminalStatus = $kick.status }

$disconnected = $false
for ($attempt = 1; $attempt -le 30; $attempt++) {
    Start-Sleep -Seconds 1
    try {
        $latestOnline = Invoke-PanelJson 'GET' $onlineUri $headers
        $remaining = Get-OnlineMatch @($latestOnline.players)
    }
    catch { Stop-Blocked 'Disconnect polling failed.' }
    if ($remaining.Count -eq 0) { $disconnected = $true; break }
    if ($remaining.Count -ne 1) { Stop-Blocked 'Disconnect polling found an ambiguous stable identity.' }
}
if (-not $disconnected) { Stop-Blocked 'Controlled player remained connected after the kick deadline.' }
$summary.steps += [ordered]@{ name = 'disconnect'; status = 'Passed' }

$auditUri = $ApiBaseUrl.TrimEnd('/') + '/api/v1/audit?sourceKind=playerAction&limit=200'
$audited = $false
for ($attempt = 1; $attempt -le 30; $attempt++) {
    Start-Sleep -Seconds 1
    try { $audit = Invoke-PanelJson 'GET' $auditUri $headers }
    catch { Stop-Blocked 'Audit polling failed.' }
    $entry = @($audit.entries | Where-Object { $_.sourceId -ceq $kick.operationId -and $_.status -ceq 'Succeeded' })
    if ($entry.Count -eq 1) { $audited = $true; break }
    if ($entry.Count -gt 1) { Stop-Blocked 'Audit polling found duplicate terminal entries.' }
}
if (-not $audited) { Stop-Blocked 'Terminal player-action audit was not observed before the deadline.' }
$summary.steps += [ordered]@{ name = 'audit'; status = 'Passed'; operationId = $kick.operationId; terminalStatus = 'Succeeded' }
$summary.status = 'Passed'
Write-Evidence 'Passed'
Write-Output (Get-Content -LiteralPath $summaryPath -Raw)
