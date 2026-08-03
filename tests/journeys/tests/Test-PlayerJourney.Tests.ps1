$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$scriptPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Test-PlayerJourney.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-player-journey-' + [Guid]::NewGuid().ToString('N'))
function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Invoke-Checker([string] $EvidenceDirectory, [switch] $Confirm) {
    $oldToken = $env:PANEL_ACCESS_TOKEN
    Remove-Item Env:PANEL_ACCESS_TOKEN -ErrorAction SilentlyContinue
    try {
        if ($Confirm) { & $scriptPath -ExpectedCrossplatformId 'EOS_TEST' -EnvironmentId 'fixture' -EvidenceDirectory $EvidenceDirectory -ConfirmKickTestPlayer | Out-Null }
        else { & $scriptPath -ExpectedCrossplatformId 'EOS_TEST' -EnvironmentId 'fixture' -EvidenceDirectory $EvidenceDirectory | Out-Null }
        return $LASTEXITCODE
    }
    finally {
        if ($null -eq $oldToken) { Remove-Item Env:PANEL_ACCESS_TOKEN -ErrorAction SilentlyContinue }
        else { $env:PANEL_ACCESS_TOKEN = $oldToken }
    }
}
try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    foreach ($confirm in @($false, $true)) {
        $evidence = Join-Path $temporaryRoot ('evidence-' + $confirm)
        $exitCode = Invoke-Checker $evidence -Confirm:$confirm
        Assert-True ($exitCode -eq 2) 'Missing access token must result in a skipped non-zero safety outcome.'
        $run = Get-ChildItem -LiteralPath $evidence -Directory | Select-Object -First 1
        Assert-True ($null -ne $run) 'Skipped player journey must create evidence directory.'
        $summaryText = Get-Content -LiteralPath (Join-Path $run.FullName 'summary.json') -Raw
        $summary = $summaryText | ConvertFrom-Json
        Assert-True ($summary.status -eq 'Skipped') 'Missing token must be recorded as Skipped.'
        Assert-True ($summaryText -notmatch 'EOS_TEST') 'Evidence must redact the stable identity.'
        Assert-True ($summary.expectedCrossplatformIdFingerprint -match '^[0-9a-f]{64}$') 'Evidence must retain only an identity fingerprint.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $run.FullName 'manifest.json') -Raw) -notmatch 'PANEL_ACCESS_TOKEN') 'Manifest must not contain token names or values.'
    }
    $failed = $false
    try { & $scriptPath -ExpectedCrossplatformId 'EOS_TEST' -EnvironmentId 'fixture' -EvidenceDirectory ([System.IO.Path]::GetPathRoot($temporaryRoot)) | Out-Null } catch { $failed = $true }
    Assert-True $failed 'Filesystem root evidence must be rejected.'
    Write-Host 'Player journey safety tests passed.'
}
finally { if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force } }
