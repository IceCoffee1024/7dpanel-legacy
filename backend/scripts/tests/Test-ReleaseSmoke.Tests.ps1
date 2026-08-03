$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$sourceRoot = Split-Path $PSScriptRoot -Parent
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-release-smoke-tests-' + [Guid]::NewGuid().ToString('N'))
$scriptRoot = Join-Path $temporaryRoot 'scripts'
$evidenceRoot = Join-Path $temporaryRoot 'evidence'
$tracePath = Join-Path $temporaryRoot 'trace.txt'
$env:SEVENDPANEL_SMOKE_TEST_TRACE = $tracePath
$env:SEVENDPANEL_SMOKE_TEST_FAIL_STEP = ''
$env:SEVENDPANEL_SMOKE_TEST_NATIVE_EXIT_STEP = ''

function Assert-True {
    param(
        [Parameter(Mandatory = $true)] [bool] $Condition,
        [Parameter(Mandatory = $true)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)] [string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Get-OnlyRunDirectory {
    param([Parameter(Mandatory = $true)] [string] $Root)

    $directories = @(Get-ChildItem -LiteralPath $Root -Directory)
    Assert-Equal 1 $directories.Count 'Expected exactly one smoke evidence run directory.'
    return $directories[0].FullName
}

try {
    New-Item -ItemType Directory -Path $scriptRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'Test-ReleaseSmoke.ps1') -Destination $scriptRoot
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'New-EvidenceManifest.ps1') -Destination $scriptRoot
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'Get-ReleaseArtifactIdentity.ps1') -Destination $scriptRoot
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'release-manifest.json') -Destination $scriptRoot

    $stubs = @{
        'Stop-Server.ps1' = @'
param([int] $TimeoutSeconds, [string] $EnvironmentFile, [switch] $Local, [string] $ComputerName, [int] $TelnetPort, [System.Management.Automation.PSCredential] $Credential)
Add-Content -LiteralPath $env:SEVENDPANEL_SMOKE_TEST_TRACE -Value 'Stop-Server'
Write-Host 'Authorization: Bearer full-authorization-value'
Write-Host 'Password=full-password-value'
if ($Credential) { Write-Host $Credential.GetNetworkCredential().Password }
if ($env:SEVENDPANEL_SMOKE_TEST_FAIL_STEP -eq 'Stop-Server') { throw 'Secret: full-failure-secret' }
'Stopped'
'@
        'Publish-Mod.ps1' = @'
param([string] $EnvironmentFile, [string] $PublishDirectory)
Add-Content -LiteralPath $env:SEVENDPANEL_SMOKE_TEST_TRACE -Value 'Publish-Mod'
Write-Host 'X-API-Key: full-api-key-value'
if ($env:SEVENDPANEL_SMOKE_TEST_FAIL_STEP -eq 'Publish-Mod') { throw 'Secret: full-failure-secret' }
if ($env:SEVENDPANEL_SMOKE_TEST_NATIVE_EXIT_STEP -eq 'Publish-Mod') {
    $hostExecutable = (Get-Process -Id $PID).Path
    & $hostExecutable -NoProfile -Command 'exit 23'
}
'Published'
'@
        'Start-Server.ps1' = @'
param([int] $TimeoutSeconds, [string] $EnvironmentFile, [switch] $Local, [string] $ComputerName, [string] $ServerRoot, [System.Management.Automation.PSCredential] $Credential)
Add-Content -LiteralPath $env:SEVENDPANEL_SMOKE_TEST_TRACE -Value 'Start-Server'
Write-Host '{"access_token":"full-json-token","safe":"visible"}'
if ($env:SEVENDPANEL_SMOKE_TEST_FAIL_STEP -eq 'Start-Server') { throw 'Secret: full-failure-secret' }
'Started'
'@
        'Test-HealthEndpoint.ps1' = @'
param([int] $TimeoutSeconds, [string] $EnvironmentFile, [string] $HealthUrl)
Add-Content -LiteralPath $env:SEVENDPANEL_SMOKE_TEST_TRACE -Value 'Test-HealthEndpoint'
Write-Host 'HTTP 200 https://example.invalid/health?api_key=full-query-key&safe=1'
if ($env:SEVENDPANEL_SMOKE_TEST_FAIL_STEP -eq 'Test-HealthEndpoint') { throw 'Secret: full-failure-secret' }
'Healthy'
'@
    }
    foreach ($stub in $stubs.GetEnumerator()) {
        Set-Content -LiteralPath (Join-Path $scriptRoot $stub.Key) -Value $stub.Value -Encoding UTF8
    }

    $smokePath = Join-Path $scriptRoot 'Test-ReleaseSmoke.ps1'

    & $smokePath | Out-Null
    Assert-True (-not (Test-Path -LiteralPath $evidenceRoot)) 'Default smoke behavior must not create evidence.'
    Assert-Equal 'Stop-Server,Publish-Mod,Start-Server,Test-HealthEndpoint' ((Get-Content -LiteralPath $tracePath) -join ',') 'Default smoke sequence changed.'

    Remove-Item -LiteralPath $tracePath
    $credentialPassword = ConvertTo-SecureString 'unlabeled-credential-password' -AsPlainText -Force
    $credential = New-Object System.Management.Automation.PSCredential ('smoke-test-user', $credentialPassword)
    & $smokePath -EvidenceDirectory $evidenceRoot -Credential $credential | Out-Null
    $runDirectory = Get-OnlyRunDirectory $evidenceRoot
    $summaryPath = Join-Path $runDirectory 'summary.json'
    $manifestPath = Join-Path $runDirectory 'manifest.json'
    $summaryBytes = [System.IO.File]::ReadAllBytes($summaryPath)
    $hasUtf8Bom = $summaryBytes.Length -ge 3 -and
        $summaryBytes[0] -eq 0xEF -and
        $summaryBytes[1] -eq 0xBB -and
        $summaryBytes[2] -eq 0xBF
    Assert-True (-not $hasUtf8Bom) 'Smoke summary must use portable UTF-8 without a BOM.'
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    Assert-True (Test-Path -LiteralPath $manifestPath) 'Evidence-enabled smoke must create a manifest.'
    $evidenceManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Equal 'release-smoke' $evidenceManifest.evidenceKind 'Smoke manifest evidence kind is incorrect.'
    Assert-Equal 'Passed' $evidenceManifest.status 'Successful smoke manifest status is incorrect.'
    Assert-Equal 'Development' $evidenceManifest.maturity 'Smoke manifest must not promote maturity.'
    Assert-True ($evidenceManifest.environmentId -match '^[A-F0-9]{64}$') 'Smoke manifest must store a non-sensitive environment digest.'
    Assert-Equal 'summary.json,01-stop-server.log,02-publish-mod.log,03-start-server.log,04-health-endpoint.log' (@($evidenceManifest.subEvidence) -join ',') 'Smoke manifest sub-evidence is incorrect.'
    Assert-Equal 'Passed' $summary.status 'Successful smoke summary status is incorrect.'
    Assert-Equal 0 $summary.exitCode 'Successful smoke summary exit code is incorrect.'
    Assert-True ($summary.durationMilliseconds -ge 0) 'Successful smoke summary duration is missing.'
    Assert-Equal 4 @($summary.steps).Count 'Successful smoke summary step count is incorrect.'
    Assert-Equal 'Stop-Server,Publish-Mod,Start-Server,Test-HealthEndpoint' ((@($summary.steps) | ForEach-Object { $_.name }) -join ',') 'Evidence smoke sequence changed.'
    foreach ($step in @($summary.steps)) {
        Assert-Equal 'Passed' $step.status "Step $($step.name) status is incorrect."
        Assert-Equal 0 $step.exitCode "Step $($step.name) exit code is incorrect."
        Assert-True ([DateTimeOffset]::Parse($step.startedAtUtc) -le [DateTimeOffset]::Parse($step.endedAtUtc)) "Step $($step.name) timestamps are invalid."
        Assert-True (Test-Path -LiteralPath (Join-Path $runDirectory $step.logFile)) "Step $($step.name) log is missing."
    }
    $allEvidence = (@(Get-ChildItem -LiteralPath $runDirectory -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw })) -join "`n"
    foreach ($secret in @('full-authorization-value', 'full-password-value', 'full-api-key-value', 'full-json-token', 'full-query-key', 'unlabeled-credential-password')) {
        Assert-True (-not $allEvidence.Contains($secret)) "Evidence retained secret value '$secret'."
    }
    Assert-True ($allEvidence.Contains('<redacted>')) 'Evidence did not contain redaction markers.'
    Assert-True ($allEvidence.Contains('"safe":"visible"')) 'Evidence removed non-sensitive diagnostic content.'

    $failedEvidenceRoot = Join-Path $temporaryRoot 'failed-evidence'
    $env:SEVENDPANEL_SMOKE_TEST_FAIL_STEP = 'Start-Server'
    $failed = $false
    try {
        & $smokePath -EvidenceDirectory $failedEvidenceRoot | Out-Null
    }
    catch {
        $failed = $true
    }
    Assert-True $failed 'A failed smoke step must fail the orchestrator.'
    $failedRunDirectory = Get-OnlyRunDirectory $failedEvidenceRoot
    $failedSummary = Get-Content -LiteralPath (Join-Path $failedRunDirectory 'summary.json') -Raw | ConvertFrom-Json
    $failedManifest = Get-Content -LiteralPath (Join-Path $failedRunDirectory 'manifest.json') -Raw | ConvertFrom-Json
    Assert-Equal 'Failed' $failedManifest.status 'Failed smoke manifest status is incorrect.'
    Assert-Equal 'summary.json,01-stop-server.log,02-publish-mod.log,03-start-server.log' (@($failedManifest.subEvidence) -join ',') 'Failed smoke manifest must retain only attempted-step evidence.'
    Assert-Equal 'Failed' $failedSummary.status 'Failed smoke summary status is incorrect.'
    Assert-Equal 1 $failedSummary.exitCode 'Failed smoke summary exit code is incorrect.'
    Assert-True ($failedSummary.durationMilliseconds -ge 0) 'Failed smoke summary duration is missing.'
    Assert-Equal 3 @($failedSummary.steps).Count 'Smoke must stop after the failed step.'
    Assert-Equal 'Failed' $failedSummary.steps[2].status 'Failed step status is incorrect.'
    Assert-Equal 1 $failedSummary.steps[2].exitCode 'Failed step exit code is incorrect.'
    $failedLog = Get-Content -LiteralPath (Join-Path $failedRunDirectory $failedSummary.steps[2].logFile) -Raw
    Assert-True (-not $failedLog.Contains('full-failure-secret')) 'Failed step log retained a secret.'
    Assert-True ($failedLog.Contains('Secret: <redacted>')) 'Failed step log omitted its redacted diagnostic.'

    $env:SEVENDPANEL_SMOKE_TEST_FAIL_STEP = ''
    $env:SEVENDPANEL_SMOKE_TEST_NATIVE_EXIT_STEP = 'Publish-Mod'
    $nativeExitFailed = $false
    Remove-Item -LiteralPath $tracePath
    try {
        & $smokePath | Out-Null
    }
    catch {
        $nativeExitFailed = $true
    }
    Assert-True $nativeExitFailed 'A non-zero child native exit code must fail without evidence enabled.'
    Assert-Equal 'Stop-Server,Publish-Mod' ((Get-Content -LiteralPath $tracePath) -join ',') 'Smoke continued after a native command failure.'

    $nativeExitEvidenceRoot = Join-Path $temporaryRoot 'native-exit-evidence'
    $nativeExitFailed = $false
    try {
        & $smokePath -EvidenceDirectory $nativeExitEvidenceRoot | Out-Null
    }
    catch {
        $nativeExitFailed = $true
    }
    Assert-True $nativeExitFailed 'A non-zero child native exit code must fail the orchestrator.'
    $nativeExitRunDirectory = Get-OnlyRunDirectory $nativeExitEvidenceRoot
    $nativeExitSummary = Get-Content -LiteralPath (Join-Path $nativeExitRunDirectory 'summary.json') -Raw | ConvertFrom-Json
    Assert-Equal 'Failed' $nativeExitSummary.status 'Native-exit smoke summary status is incorrect.'
    Assert-Equal 23 $nativeExitSummary.exitCode 'Native-exit smoke summary did not preserve the exit code.'
    Assert-Equal 2 @($nativeExitSummary.steps).Count 'Smoke must stop after a native command failure.'
    Assert-Equal 'Failed' $nativeExitSummary.steps[1].status 'Native-exit step status is incorrect.'
    Assert-Equal 23 $nativeExitSummary.steps[1].exitCode 'Native-exit step did not preserve the exit code.'

    Write-Host 'Release smoke evidence tests passed.'
}
finally {
    Remove-Item Env:\SEVENDPANEL_SMOKE_TEST_TRACE -ErrorAction SilentlyContinue
    Remove-Item Env:\SEVENDPANEL_SMOKE_TEST_FAIL_STEP -ErrorAction SilentlyContinue
    Remove-Item Env:\SEVENDPANEL_SMOKE_TEST_NATIVE_EXIT_STEP -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
