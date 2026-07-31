[CmdletBinding()]
param(
    [string] $ComputerName,
    [string] $ServerRoot,
    [int] $TelnetPort,
    [System.Management.Automation.PSCredential] $Credential,
    [switch] $Local,
    [ValidateRange(1, 600)]
    [int] $StopTimeoutSeconds = 60,
    [ValidateRange(1, 300)]
    [int] $StartTimeoutSeconds = 30,
    [string] $HealthUrl,
    [ValidateRange(1, 600)]
    [int] $HealthTimeoutSeconds = 90,
    [string] $EnvironmentFile,
    [string] $PublishDirectory
)

$ErrorActionPreference = 'Stop'

if ($Local -and $ComputerName) {
    throw 'ComputerName cannot be combined with Local.'
}

$hasLifecycleTargetOverride =
    $Local -or
    $PSBoundParameters.ContainsKey('ComputerName') -or
    $PSBoundParameters.ContainsKey('ServerRoot')
if ($hasLifecycleTargetOverride -and
    -not $PSBoundParameters.ContainsKey('PublishDirectory')) {
    throw 'PublishDirectory is required when Local, ComputerName, or ServerRoot overrides the lifecycle target.'
}

$environmentParameters = @{}
if ($PSBoundParameters.ContainsKey('EnvironmentFile')) {
    $environmentParameters.EnvironmentFile = $EnvironmentFile
}

$stopParameters = @{ TimeoutSeconds = $StopTimeoutSeconds }
$startParameters = @{ TimeoutSeconds = $StartTimeoutSeconds }

foreach ($entry in $environmentParameters.GetEnumerator()) {
    $stopParameters[$entry.Key] = $entry.Value
    $startParameters[$entry.Key] = $entry.Value
}

if ($Local) {
    $stopParameters.Local = $true
    $startParameters.Local = $true
}
elseif ($PSBoundParameters.ContainsKey('ComputerName')) {
    $stopParameters.ComputerName = $ComputerName
    $startParameters.ComputerName = $ComputerName
}

if ($PSBoundParameters.ContainsKey('TelnetPort')) {
    $stopParameters.TelnetPort = $TelnetPort
}
if ($PSBoundParameters.ContainsKey('ServerRoot')) {
    $startParameters.ServerRoot = $ServerRoot
}
if ($PSBoundParameters.ContainsKey('Credential')) {
    $stopParameters.Credential = $Credential
    $startParameters.Credential = $Credential
}

Write-Host 'Stopping 7DTD before publishing...'
& (Join-Path $PSScriptRoot 'Stop-Server.ps1') @stopParameters

Write-Host 'Publishing the 7DPanel Mod...'
$publishParameters = @{}
foreach ($entry in $environmentParameters.GetEnumerator()) {
    $publishParameters[$entry.Key] = $entry.Value
}
if ($PSBoundParameters.ContainsKey('PublishDirectory')) {
    $publishParameters.PublishDirectory = $PublishDirectory
}
& (Join-Path $PSScriptRoot 'Publish-Mod.ps1') @publishParameters

Write-Host 'Starting 7DTD...'
& (Join-Path $PSScriptRoot 'Start-Server.ps1') @startParameters

$healthParameters = @{ TimeoutSeconds = $HealthTimeoutSeconds }
foreach ($entry in $environmentParameters.GetEnumerator()) {
    $healthParameters[$entry.Key] = $entry.Value
}
if ($PSBoundParameters.ContainsKey('HealthUrl')) {
    $healthParameters.HealthUrl = $HealthUrl
}

Write-Host 'Waiting for the 7DPanel health endpoint...'
& (Join-Path $PSScriptRoot 'Test-HealthEndpoint.ps1') @healthParameters
