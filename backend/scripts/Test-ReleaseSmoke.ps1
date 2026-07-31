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
    [string] $PublishDirectory,
    [string] $EvidenceDirectory
)

$ErrorActionPreference = 'Stop'

function Protect-SmokeEvidenceText {
    param(
        [AllowEmptyString()]
        [string] $Text,
        [string[]] $SecretValues = @()
    )

    $redacted = $Text
    foreach ($secretValue in $SecretValues) {
        if (-not [string]::IsNullOrEmpty($secretValue)) {
            $redacted = $redacted.Replace($secretValue, '<redacted>')
        }
    }

    $redacted = [regex]::Replace(
        $redacted,
        '(?i)\b(Bearer|Basic)\s+[A-Za-z0-9+/_=.-]+',
        '$1 <redacted>')
    $redacted = [regex]::Replace(
        $redacted,
        '(?i)([?&](?:api[_-]?key|authorization|password|secret|access[_-]?token|refresh[_-]?token)=)[^&\s]+',
        '$1<redacted>')
    $redacted = [regex]::Replace(
        $redacted,
        '(?i)("(?:authorization|proxy-authorization|x-api-key|api[_-]?key|password|secret|access[_-]?token|refresh[_-]?token)"\s*:\s*)"(?:\\.|[^"\\])*"',
        '$1"<redacted>"')
    $redacted = [regex]::Replace(
        $redacted,
        '(?im)(\b(?:Authorization|Proxy-Authorization|X-Api-Key|Api[-_ ]?Key|Password|Secret|Access[-_ ]?Token|Refresh[-_ ]?Token)\b\s*[:=]\s*)(?:"[^"\r\n]*"|''[^''\r\n]*''|[^\s,;}\r\n]+)',
        '$1<redacted>')
    $redacted = [regex]::Replace(
        $redacted,
        '(?i)(-(?:Password|ApiKey|Secret|AccessToken|RefreshToken)\s+)(?:"[^"\r\n]*"|''[^''\r\n]*''|\S+)',
        '$1<redacted>')

    return $redacted
}

function Write-SmokeEvidenceSummary {
    param(
        [Parameter(Mandatory = $true)] [System.Collections.IDictionary] $Summary,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $json = $Summary | ConvertTo-Json -Depth 6
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json, $utf8WithoutBom)
}

function Invoke-SmokeChildScript {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $ScriptPath,
        [Parameter(Mandatory = $true)] [hashtable] $Parameters
    )

    $global:LASTEXITCODE = 0
    $records = @(& $ScriptPath @Parameters *>&1)
    $nativeExitCode = $LASTEXITCODE
    if ($nativeExitCode -ne 0) {
        $exception = New-Object System.InvalidOperationException(
            "Smoke step '$Name' failed with native exit code $nativeExitCode.")
        $exception.Data['ExitCode'] = $nativeExitCode
        throw $exception
    }
    return $records
}

function Get-SmokeFailureExitCode {
    param([Parameter(Mandatory = $true)] [System.Management.Automation.ErrorRecord] $Failure)

    $recordedExitCode = $Failure.Exception.Data['ExitCode']
    if ($recordedExitCode -is [int] -and $recordedExitCode -ne 0) {
        return $recordedExitCode
    }
    return 1
}

function Invoke-SmokeEvidenceStep {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $ScriptPath,
        [Parameter(Mandatory = $true)] [hashtable] $Parameters,
        [Parameter(Mandatory = $true)] [string] $LogFile,
        [Parameter(Mandatory = $true)] [System.Collections.IDictionary] $Summary,
        [Parameter(Mandatory = $true)] [string] $SummaryPath,
        [string[]] $SecretValues = @()
    )

    $startedAt = [DateTimeOffset]::UtcNow
    $records = @()
    $failure = $null

    try {
        $records = @(Invoke-SmokeChildScript -Name $Name -ScriptPath $ScriptPath -Parameters $Parameters)
        $status = 'Passed'
        $exitCode = 0
    }
    catch {
        $failure = $_
        $records += $_
        $status = 'Failed'
        $exitCode = Get-SmokeFailureExitCode -Failure $_
    }

    $endedAt = [DateTimeOffset]::UtcNow
    $logText = Protect-SmokeEvidenceText -Text ($records | Out-String -Width 4096) -SecretValues $SecretValues
    Set-Content -LiteralPath $LogFile -Value $logText.TrimEnd() -Encoding UTF8

    $Summary.steps += [ordered]@{
        name = $Name
        startedAtUtc = $startedAt.ToString('o')
        endedAtUtc = $endedAt.ToString('o')
        durationMilliseconds = [long]($endedAt - $startedAt).TotalMilliseconds
        status = $status
        exitCode = $exitCode
        logFile = Split-Path $LogFile -Leaf
    }
    $Summary.endedAtUtc = $endedAt.ToString('o')
    $Summary.durationMilliseconds = [long]($endedAt - [DateTimeOffset]::Parse($Summary.startedAtUtc)).TotalMilliseconds
    $Summary.status = if ($failure) { 'Failed' } else { 'Running' }
    $Summary.exitCode = if ($failure) { $exitCode } else { $null }
    Write-SmokeEvidenceSummary -Summary $Summary -Path $SummaryPath

    foreach ($record in $records) {
        Write-Output $record
    }

    if ($failure) {
        throw $failure
    }
}

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

$evidenceEnabled = $PSBoundParameters.ContainsKey('EvidenceDirectory')
$evidenceSummary = $null
$evidenceSummaryPath = $null
$evidenceRunDirectory = $null
$secretValues = @()
if ($evidenceEnabled) {
    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        throw 'EvidenceDirectory cannot be empty when specified.'
    }

    $evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceDirectory)
    New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
    $runName = 'release-smoke-{0}-{1}' -f
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'),
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $evidenceRunDirectory = Join-Path $evidenceRoot $runName
    New-Item -ItemType Directory -Path $evidenceRunDirectory | Out-Null
    $evidenceSummaryPath = Join-Path $evidenceRunDirectory 'summary.json'
    $startedAt = [DateTimeOffset]::UtcNow
    $evidenceSummary = [ordered]@{
        schemaVersion = 1
        runId = $runName
        script = 'Test-ReleaseSmoke.ps1'
        startedAtUtc = $startedAt.ToString('o')
        endedAtUtc = $null
        durationMilliseconds = $null
        status = 'Running'
        exitCode = $null
        steps = @()
    }
    if ($PSBoundParameters.ContainsKey('Credential')) {
        $secretValues += $Credential.GetNetworkCredential().Password
    }
    Write-SmokeEvidenceSummary -Summary $evidenceSummary -Path $evidenceSummaryPath
    Write-Host "Smoke evidence directory: $evidenceRunDirectory"
}

Write-Host 'Stopping 7DTD before publishing...'
if ($evidenceEnabled) {
    Invoke-SmokeEvidenceStep -Name 'Stop-Server' `
        -ScriptPath (Join-Path $PSScriptRoot 'Stop-Server.ps1') `
        -Parameters $stopParameters `
        -LogFile (Join-Path $evidenceRunDirectory '01-stop-server.log') `
        -Summary $evidenceSummary `
        -SummaryPath $evidenceSummaryPath `
        -SecretValues $secretValues
}
else {
    Invoke-SmokeChildScript -Name 'Stop-Server' `
        -ScriptPath (Join-Path $PSScriptRoot 'Stop-Server.ps1') `
        -Parameters $stopParameters
}

Write-Host 'Publishing the 7DPanel Mod...'
$publishParameters = @{}
foreach ($entry in $environmentParameters.GetEnumerator()) {
    $publishParameters[$entry.Key] = $entry.Value
}
if ($PSBoundParameters.ContainsKey('PublishDirectory')) {
    $publishParameters.PublishDirectory = $PublishDirectory
}
if ($evidenceEnabled) {
    Invoke-SmokeEvidenceStep -Name 'Publish-Mod' `
        -ScriptPath (Join-Path $PSScriptRoot 'Publish-Mod.ps1') `
        -Parameters $publishParameters `
        -LogFile (Join-Path $evidenceRunDirectory '02-publish-mod.log') `
        -Summary $evidenceSummary `
        -SummaryPath $evidenceSummaryPath `
        -SecretValues $secretValues
}
else {
    Invoke-SmokeChildScript -Name 'Publish-Mod' `
        -ScriptPath (Join-Path $PSScriptRoot 'Publish-Mod.ps1') `
        -Parameters $publishParameters
}

Write-Host 'Starting 7DTD...'
if ($evidenceEnabled) {
    Invoke-SmokeEvidenceStep -Name 'Start-Server' `
        -ScriptPath (Join-Path $PSScriptRoot 'Start-Server.ps1') `
        -Parameters $startParameters `
        -LogFile (Join-Path $evidenceRunDirectory '03-start-server.log') `
        -Summary $evidenceSummary `
        -SummaryPath $evidenceSummaryPath `
        -SecretValues $secretValues
}
else {
    Invoke-SmokeChildScript -Name 'Start-Server' `
        -ScriptPath (Join-Path $PSScriptRoot 'Start-Server.ps1') `
        -Parameters $startParameters
}

$healthParameters = @{ TimeoutSeconds = $HealthTimeoutSeconds }
foreach ($entry in $environmentParameters.GetEnumerator()) {
    $healthParameters[$entry.Key] = $entry.Value
}
if ($PSBoundParameters.ContainsKey('HealthUrl')) {
    $healthParameters.HealthUrl = $HealthUrl
}

Write-Host 'Waiting for the 7DPanel health endpoint...'
if ($evidenceEnabled) {
    Invoke-SmokeEvidenceStep -Name 'Test-HealthEndpoint' `
        -ScriptPath (Join-Path $PSScriptRoot 'Test-HealthEndpoint.ps1') `
        -Parameters $healthParameters `
        -LogFile (Join-Path $evidenceRunDirectory '04-health-endpoint.log') `
        -Summary $evidenceSummary `
        -SummaryPath $evidenceSummaryPath `
        -SecretValues $secretValues
    $evidenceSummary.status = 'Passed'
    $evidenceSummary.exitCode = 0
    $endedAt = [DateTimeOffset]::UtcNow
    $evidenceSummary.endedAtUtc = $endedAt.ToString('o')
    $evidenceSummary.durationMilliseconds = [long]($endedAt - [DateTimeOffset]::Parse($evidenceSummary.startedAtUtc)).TotalMilliseconds
    Write-SmokeEvidenceSummary -Summary $evidenceSummary -Path $evidenceSummaryPath
}
else {
    Invoke-SmokeChildScript -Name 'Test-HealthEndpoint' `
        -ScriptPath (Join-Path $PSScriptRoot 'Test-HealthEndpoint.ps1') `
        -Parameters $healthParameters
}
